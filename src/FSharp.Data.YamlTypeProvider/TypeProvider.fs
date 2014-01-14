namespace FSharp.Configuration

#nowarn "57"

open System.Reflection
open Microsoft.FSharp.Core.CompilerServices
open Samples.FSharp.ProvidedTypes
open System
open System.Diagnostics
open Microsoft.FSharp.Quotations
open System.IO
open System.Collections.Generic
open System.Threading

module TypesFactory =
    open YamlParser

    type Scalar with
        member x.ToExpr() = 
            match x with
            | Int x -> Expr.Value x
            | String x -> Expr.Value x
            | Bool x -> Expr.Value x
            | TimeSpan x -> 
                let parse = typeof<TimeSpan>.GetMethod("Parse", [|typeof<string>|])
                Expr.Call(parse, [Expr.Value (x.ToString())])
            | Uri x ->
                let ctr = typeof<Uri>.GetConstructor [|typeof<string>|]
                Expr.NewObject(ctr, [Expr.Value x.OriginalString])

    type T =
        { MainType: Type option
          Types: MemberInfo list
          Init: Expr -> Expr }

    let private generateChangedEvent =
        let eventType = typeof<EventHandler>
        let delegateType = typeof<Delegate>
        let combineMethod = delegateType.GetMethod("Combine", [|delegateType; delegateType|])
        let removeMethod = delegateType.GetMethod("Remove", [|delegateType; delegateType|])

        fun() ->
            let eventField = ProvidedField("_changed", eventType)
            let event = ProvidedEvent("Changed", eventType)

            let changeEvent m me v = 
                let current = Expr.Coerce (Expr.FieldGet(me, eventField), delegateType)
                let other = Expr.Coerce (v, delegateType)
                Expr.Coerce (Expr.Call (m, [current; other]), eventType)

            let adder = changeEvent combineMethod
            let remover = changeEvent removeMethod

            event.AdderCode <- fun [me; v] -> Expr.FieldSet(me, eventField, adder me v)
            event.RemoverCode <- fun [me; v] -> Expr.FieldSet(me, eventField, remover me v)
            eventField, event

    let rec transform readOnly name (node: Node) =
        match name, node with
        | Some name, Scalar (_ as x) -> transformScalar readOnly name x
        | _, Map m -> transformMap readOnly name m
        | Some name, List l -> transformList readOnly name l
        | None, _ -> failwithf "Only Maps are allowed at the root level."
    
    and transformScalar readOnly name (node: Scalar) =
        let rawType = node.UnderlyingType
        let field = ProvidedField("_" +  name, rawType)
        let prop = ProvidedProperty (name, rawType, IsStatic=false, GetterCode = (fun [me] -> Expr.FieldGet(me, field)))
        if not readOnly then prop.SetterCode <- (fun [me;v] -> Expr.FieldSet(me, field, v))
        let initValue = node.ToExpr()

        { MainType = Some rawType
          Types = [field :> MemberInfo; prop :> MemberInfo]
          Init = fun me -> Expr.FieldSet(me, field, initValue) }

    and transformList readOnly name (children: Node list) =
        let elements = 
            children 
            |> List.map (function
               | Scalar x -> { MainType = Some x.UnderlyingType; Types = []; Init = fun _ -> x.ToExpr() }
               | Map m -> transformMap readOnly None m)

        let elementType = 
            match elements |> Seq.groupBy (fun n -> n.MainType) |> Seq.map fst |> Seq.toList with
            | [Some ty] -> ty
            | types -> failwithf "List cannot contain elements of heterohenius types (attempt to mix types: %A)." 
                                 (types |> List.map (Option.map (fun x -> x.Name)))

        let fieldType = typedefof<ResizeArray<_>>.MakeGenericType elementType
        
        let propType =
            (match readOnly with
             | true -> typedefof<IReadOnlyList<_>>
             | false -> typedefof<IList<_>>).MakeGenericType elementType

        let field = ProvidedField("_" + name, fieldType)
        let prop = ProvidedProperty (name, propType, IsStatic=false, GetterCode = (fun [me] -> Expr.Coerce(Expr.FieldGet(me, field), propType)))
        let listCtr = fieldType.GetConstructor([|typedefof<seq<_>>.MakeGenericType elementType|])
        if not readOnly then prop.SetterCode <- fun [me;v] -> Expr.FieldSet(me, field, Expr.NewObject(listCtr, [v]))
        let childTypes = elements |> List.collect (fun x -> x.Types)
        let initValue me = Expr.NewObject(listCtr, [Expr.NewArray(elementType, elements |> List.map (fun x -> x.Init me))])

        { MainType = Some fieldType
          Types = childTypes @ [field :> MemberInfo; prop :> MemberInfo]
          Init = fun me -> Expr.FieldSet(me, field, initValue me) }

    and foldChildren readOnly (children: (string * Node) list) =
        let childTypes, childInits =
            children
            |> List.map (fun (name, node) -> transform readOnly (Some name) node)
            |> List.fold (fun (types, inits) t -> types @ t.Types, inits @ [t.Init]) ([], [])

        let affinedChildInits me =
            childInits 
            |> List.fold (fun acc expr -> expr me :: acc) []
            |> List.reduce (fun res expr -> Expr.Sequential(res, expr))
        childTypes, affinedChildInits

    and transformMap readOnly name (children: (string * Node) list) =
        let childTypes, childInits = foldChildren readOnly children
        let eventField, event = generateChangedEvent()
        match name with
        | Some name ->
            let mapTy = ProvidedTypeDefinition(name + "_Type", Some typeof<obj>, HideObjectMethods=true, 
                                               IsErased=false, SuppressRelocation=false)
            let ctr = ProvidedConstructor([], InvokeCode = (fun [me] -> childInits me))
            mapTy.AddMembers (ctr :> MemberInfo :: childTypes)
            let field = ProvidedField("_" + name, mapTy)
            let prop = ProvidedProperty (name, mapTy, IsStatic=false, GetterCode = (fun [me] -> Expr.FieldGet(me, field)))
            
//            let eventArgsTy = ProvidedTypeDefinition("ChangedEventArgs", Some typeof<EventArgs>, IsErased=false, SuppressRelocation=false)
//            let eventType = typedefof<EventHandler<_>>.MakeGenericType eventArgsTy
//            let eventField = ProvidedField("_onChanged", eventType)
//            let event = ProvidedEvent("Changed", eventType)
//            let delegateType = typeof<Delegate>
//            let combineMethod = delegateType.GetMethod("Combine", [|delegateType; delegateType|])
//            let removeMethod = delegateType.GetMethod("Remove", [|delegateType; delegateType|])
//
//            let changeEvent m me v = 
//                let current = Expr.Coerce (Expr.FieldGet(me, eventField), delegateType)
//                let other = Expr.Coerce (v, delegateType)
//                Expr.Coerce (Expr.Call (m, [current; other]), eventType)
//
//            let adder = changeEvent combineMethod
//            let remover = changeEvent removeMethod
//
//            event.AdderCode <- fun [me; v] -> Expr.FieldSet(me, eventField, adder me v)
//            event.RemoverCode <- fun [me; v] -> Expr.FieldSet(me, eventField, remover me v)
//
//            mapTy.AddMember eventArgsTy
//            mapTy.AddMember eventField
//            mapTy.AddMember event

            mapTy.AddMember eventField
            mapTy.AddMember event

            { MainType = Some (mapTy :> _)
              Types = [mapTy :> MemberInfo; field :> MemberInfo; prop :> MemberInfo]
              Init = fun me -> Expr.FieldSet(me, field, Expr.NewObject(ctr, [])) }
        | None -> { MainType = None; Types = [eventField :> MemberInfo; event :> MemberInfo] @ childTypes; Init = childInits }

[<TypeProvider>]
type public YamlProvider (cfg: TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()
    let mutable watcher: IDisposable option = None
//    static let log = 
//        let w = new StreamWriter @"l:\Yaml.log"
//        fun msg -> lock w |> fun _ -> 
//            w.WriteLine (sprintf "[%O, %d] %s" DateTime.Now Thread.CurrentThread.ManagedThreadId msg)
//            w.Flush()

    let disposeWatcher() =
        watcher |> Option.iter (fun x -> x.Dispose())
        watcher <- None

    let thisAssembly = Assembly.GetExecutingAssembly()
    let nameSpace = this.GetType().Namespace
    let baseTy = typeof<Root>
    
    let watchForChanges (fileName: string) =
        disposeWatcher()
        
        let fileName =
            match Path.IsPathRooted fileName with
            | true -> fileName
            | _ -> Path.Combine (cfg.ResolutionFolder, fileName)
        
        watcher <- Some (File.watch false fileName this.Invalidate)

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
                let types = TypesFactory.transform readOnly None (YamlParser.parse yaml)
                let ctr = ProvidedConstructor ([], InvokeCode = fun [me] -> types.Init me)
//                match filePath with
//                | Some filePath ->
//                    let saveMethod = 
//                        ProvidedMethod("Save", [], typeof<unit>, 
//                                       InvokeCode = fun [me] -> <@@ (%%me: Root).Save (filePath = filePath) @@>)
//                    saveMethod.AddXmlDocDelayed (fun _ -> sprintf "Saves content into %s." filePath)
//                    ty.AddMember saveMethod
//                | None -> ()
                ty.AddMembers (ctr :> MemberInfo :: types.Types)
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
    interface IDisposable with 
        member x.Dispose() = 
            //log "disposing"
            disposeWatcher()

[<TypeProviderAssembly>]
do ()
