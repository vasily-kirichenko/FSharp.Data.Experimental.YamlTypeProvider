namespace FSharp.Data.Experimental.YamlTypeProvider

open System.Reflection
open Microsoft.FSharp.Core.CompilerServices
open Samples.FSharp.ProvidedTypes
open System
open Microsoft.FSharp.Quotations
open System.IO
open System.Collections.Generic

module Yaml =
    open YamlDotNet.Core
    open YamlDotNet.RepresentationModel

    type Scalar =
        | Int of int
        | String of string
        | TimeSpan of TimeSpan
        | Bool of bool
        static member Parse (value: string) =
            match bool.TryParse value with
            | true, x -> Bool x
            | _ ->
                match Int32.TryParse value with
                | true, x -> Int x
                | _ -> match TimeSpan.TryParse value with
                       | true, x -> TimeSpan x
                       | _ -> String value
        member x.UnderlyingType = 
            match x with
            | Int x -> x.GetType()
            | String x -> x.GetType()
            | Bool x -> x.GetType()
            | TimeSpan x -> x.GetType()
        member x.ToExpr() = 
            match x with
            | Int x -> Expr.Value x
            | String x -> Expr.Value x
            | Bool x -> Expr.Value x
            | TimeSpan x -> 
                let parse = typeof<TimeSpan>.GetMethod("Parse", [|typeof<string>|])
                Expr.Call(parse, [Expr.Value (x.ToString())])

    type Node =
        | Scalar of Scalar
        | List of Node list
        | Map of (string * Node) list

    let parse text =
        let rec loop (n: YamlNode) =
            match n with
            | :? YamlScalarNode as n -> Scalar (Scalar.Parse n.Value)
            | :? YamlSequenceNode as n -> List (n.Children |> Seq.map loop |> Seq.toList)
            | :? YamlMappingNode as n -> 
                Map (n.Children |> Seq.choose (fun p -> 
                    match p.Key with
                    | :? YamlScalarNode as keyNode -> Some (keyNode.Value, loop p.Value)
                    | _ -> None) |> Seq.toList)
            | x -> failwithf "Unsupported YAML node type: %s" (x.GetType().Name)

        let stream = YamlStream()
        use reader = new StringReader(text)
        stream.Load(reader)
        let doc = stream.Documents.[0]
        loop doc.RootNode

module TypesFactory =
    type T =
        { MainType: Type option
          Types: MemberInfo list
          Init: Expr -> Expr }

    let rec transform readOnly name (node: Yaml.Node) =
        match name, node with
        | Some name, Yaml.Scalar (_ as x) -> transformScalar readOnly name x
        | _, Yaml.Map m -> transformMap readOnly name m
        | Some name, Yaml.List l -> transformList readOnly name l
        | None, _ -> failwithf "Only Maps are allowed at the root level."
    
    and transformScalar readOnly name (node: Yaml.Scalar) =
        let rawType = node.UnderlyingType
        let field = ProvidedField("_" +  name, rawType)
        let prop = ProvidedProperty (name, rawType, IsStatic=false, GetterCode = (fun [me] -> Expr.FieldGet(me, field)))
        if not readOnly then prop.SetterCode <- (fun [me;v] -> Expr.FieldSet(me, field, v))
        let initValue = node.ToExpr()

        { MainType = Some rawType
          Types = [field :> MemberInfo; prop :> MemberInfo]
          Init = fun me -> Expr.FieldSet(me, field, initValue) }

    and transformList readOnly name (children: Yaml.Node list) =
        let elements = 
            children 
            |> List.map (function
               | Yaml.Scalar x -> { MainType = Some x.UnderlyingType; Types = []; Init = fun _ -> x.ToExpr() }
               | Yaml.Map m -> transformMap readOnly None m)

        let elementType = 
            match elements |> Seq.groupBy (fun n -> n.MainType) |> Seq.map fst |> Seq.toList with
            | [Some ty] -> ty
            | types -> failwithf "List cannot contain elements of heterohenius types (attemp to mix types: %A)." 
                                 (types |> List.map (Option.map (fun x -> x.Name)))

        let fieldType = typedefof<ResizeArray<_>>.MakeGenericType elementType
        let propType = typedefof<IReadOnlyList<_>>.MakeGenericType elementType
        let field = ProvidedField("_" +  name, fieldType)
        let prop = ProvidedProperty (name, propType, IsStatic=false, GetterCode = (fun [me] -> Expr.Coerce(Expr.FieldGet(me, field), propType)))
        let listCtr = fieldType.GetConstructor([|typedefof<seq<_>>.MakeGenericType elementType|])
        if not readOnly then 
            prop.SetterCode <- fun [me;v] -> Expr.FieldSet(me, field, Expr.NewObject(listCtr, [v]))

        let childTypes = elements |> List.collect (fun x -> x.Types)
        let initValue me = Expr.NewObject(listCtr, [Expr.NewArray(elementType, elements |> List.map (fun x -> x.Init me))])

        { MainType = Some fieldType
          Types = childTypes @ [field :> MemberInfo; prop :> MemberInfo]
          Init = fun me -> Expr.FieldSet(me, field, initValue me) }

    and foldChildren readOnly (children: (string * Yaml.Node) list) =
        let childTypes, childInits =
            children
            |> List.map (fun (name, node) -> transform readOnly (Some name) node)
            |> List.fold (fun (types, inits) t -> types @ t.Types, inits @ [t.Init]) ([], [])

        let affinedChildInits me =
            childInits 
            |> List.fold (fun acc expr -> expr me :: acc) []
            |> List.reduce (fun res expr -> Expr.Sequential(res, expr))
        childTypes, affinedChildInits

    and transformMap readOnly name (children: (string * Yaml.Node) list) =
        let childTypes, childInits = foldChildren readOnly children
        match name with
        | Some name ->
            let mapTy = ProvidedTypeDefinition(name, Some typeof<obj>, HideObjectMethods=true, 
                                               IsErased=false, SuppressRelocation=false)
            let ctr = ProvidedConstructor([], InvokeCode = (fun [me] -> childInits me))
            mapTy.AddMembers (ctr :> MemberInfo :: childTypes)
            let field = ProvidedField("_" + name, mapTy)

            let prop = ProvidedProperty (name, mapTy, IsStatic=false, 
                                         GetterCode = (fun [me] -> Expr.FieldGet(me, field)),
                                         SetterCode = (fun [me;v] -> Expr.FieldSet(me, field, v)))

            { MainType = Some (mapTy :> _)
              Types = [mapTy :> MemberInfo; field :> MemberInfo; prop :> MemberInfo]
              Init = fun me -> Expr.FieldSet(me, field, Expr.NewObject(ctr, [])) }
        | None -> { MainType = None; Types = childTypes; Init = childInits }

type Root(filePath: string) = 
    member x.Save (stream: Stream) =
        let serializer = YamlDotNet.RepresentationModel.Serialization.Serializer()
        use writer = new StreamWriter(stream)
        serializer.Serialize(writer, x)
    member x.Save() =
        use file = new FileStream(filePath, FileMode.Create)
        x.Save file

[<TypeProvider>]
type public YamlProvider (cfg: TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    let thisAssembly = Assembly.GetExecutingAssembly()
    let nameSpace = this.GetType().Namespace
    let baseTy = typeof<Root>
    
    let watchForChanges (fileName: string) = 
        let path = Path.GetDirectoryName fileName
        let name = Path.GetFileName fileName
        //File.WriteAllText(@"e:\999999.txt", sprintf "ResolvedFileName = %s, Path = %s, Name = %s" fileName path name)
        let watcher = new FileSystemWatcher(Filter = name, Path = path)
        watcher.Changed.Add(fun _ -> this.Invalidate())
//        watcher.Changed.Add(fun e -> 
//            use w = new StreamWriter(@"e:\999999.txt", true)
//            w.WriteLine(sprintf "%s %A" e.Name e.ChangeType))
        watcher.EnableRaisingEvents <- true

    let newT = ProvidedTypeDefinition(thisAssembly, nameSpace, "Yaml", Some baseTy, IsErased=false, SuppressRelocation=false)
    let staticParams = 
        [ ProvidedStaticParameter ("FilePath", typeof<string>, "") 
          ProvidedStaticParameter ("ReadOnly", typeof<bool>, false)
          ProvidedStaticParameter ("YamlText", typeof<string>, "") ]

    do newT.AddXmlDoc 
        """<summary>Generate types for read/write access to a YAML file.</summary>
           <param name='FilePath'>Path to a YAML file.</param>
           <param name='ReadOnly'>Whether the rusulting properties will be read-only or not.</param>
           <param name='YamlText'>Yaml as text. Mutually exclusive with FilePath parameter.</param>"""

    do newT.DefineStaticParameters(
        parameters = staticParams,
        instantiationFunction = fun typeName paramValues ->
            let createTy yaml readOnly filePath =
                let ty = ProvidedTypeDefinition (thisAssembly, nameSpace, typeName, Some baseTy, IsErased=false, 
                                                 SuppressRelocation=false, HideObjectMethods=true)
                let { TypesFactory.Types = childTypes; TypesFactory.Init = init} = TypesFactory.transform readOnly None (Yaml.parse yaml)
                let rootCtr = typeof<Root>.GetConstructor [|typeof<string>|]
                let ctr = ProvidedConstructor ([], InvokeCode = fun [me] -> init me)
                match filePath with
                | Some filePath -> ctr.BaseConstructorCall <- fun [me] -> rootCtr, [ <@@ filePath @@> ]
                | None -> ()
                ty.AddMembers (ctr :> MemberInfo :: childTypes)
                let assemblyPath = Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".dll")
                let assembly = ProvidedAssembly assemblyPath 
                assembly.AddTypes [ty]
                ty

            match paramValues with
            | [| :? string as filePath; :? bool as readOnly; :? string as yamlText |] -> 
                 match filePath, yamlText with
                 | "", "" -> failwith "You must specify either FilePath or YamlText parameter."
                 | "", yamlText -> createTy yamlText readOnly None
                 | filePath, _ -> 
                      let filePath =
                          if Path.IsPathRooted filePath then filePath 
                          else Path.Combine(cfg.ResolutionFolder, filePath)
                      watchForChanges filePath
                      createTy (File.ReadAllText filePath) readOnly (Some filePath)
            | _ -> failwith "Wrong parameters")
    
    do this.AddNamespace(nameSpace, [newT])

[<TypeProviderAssembly>]
do ()
