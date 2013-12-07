namespace FSharp.Data.Experimental

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
        match name with
        | Some name ->
            let mapTy = ProvidedTypeDefinition(name + "_Type", Some typeof<obj>, HideObjectMethods=true, 
                                               IsErased=false, SuppressRelocation=false)
            let ctr = ProvidedConstructor([], InvokeCode = (fun [me] -> childInits me))
            mapTy.AddMembers (ctr :> MemberInfo :: childTypes)
            let field = ProvidedField("_" + name, mapTy)
            let prop = ProvidedProperty (name, mapTy, IsStatic=false, GetterCode = (fun [me] -> Expr.FieldGet(me, field)))
            
//            let eventFieldType = typedefof<Event<_>>.MakeGenericType mapTy
//            let eventField = ProvidedField("_onChanged", eventFieldType)
//            let eventType = typedefof<EventHandler<_>>.MakeGenericType mapTy
//            let event = ProvidedEvent(name + "Changed", eventType, 
//                                      AdderCode = (fun [me;value] -> Expr.FieldSet(eventField, value))
//            mapTy.AddMember event

            { MainType = Some (mapTy :> _)
              Types = [mapTy :> MemberInfo; field :> MemberInfo; prop :> MemberInfo]
              Init = fun me -> Expr.FieldSet(me, field, Expr.NewObject(ctr, [])) }
        | None -> { MainType = None; Types = childTypes; Init = childInits }

[<TypeProvider>]
type public YamlProvider (cfg: TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()
    let mutable watcher: FileSystemWatcher option = None
    static let log = 
        let w = new StreamWriter @"l:\Yaml.log"
        fun msg -> lock w |> fun _ -> 
            w.WriteLine (sprintf "[%O, %d] %s" DateTime.Now Thread.CurrentThread.ManagedThreadId msg)
            w.Flush()

    let disposeWatcher() =
        watcher |> Option.iter (fun x -> (x :> IDisposable).Dispose())
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

        let getLastWrite() = 
            if File.Exists fileName 
            then Some ((File.GetLastWriteTime fileName).Ticks) 
            else None
        
        let lastWrite = ref (getLastWrite())
        let path = Path.GetDirectoryName fileName
        let name = Path.GetFileName fileName
        let w = new FileSystemWatcher(Filter = name, Path = path)

// change
//[07.12.2013 12:46:14, 36] Deleted (Deleted). LWT = 01.01.1601 4:00:00 (504911376000000000) 
//[07.12.2013 12:46:14, 36] Renamed (Renamed). LWT = 07.12.2013 12:46:14 (635220171742164426) 
//[07.12.2013 12:46:14, 46] Changed (Changed). LWT = 07.12.2013 12:46:14 (635220171742164426)

// another change
//[07.12.2013 12:44:03, 13] Deleted (Deleted). LWT = 07.12.2013 12:44:03 (635220170437799821) 
//[07.12.2013 12:44:03, 13] Renamed (Renamed). LWT = 07.12.2013 12:44:03 (635220170437799821) 
//[07.12.2013 12:44:03, 13] Changed (Changed). LWT = 07.12.2013 12:44:03 (635220170437799821) 

// delete
//[07.12.2013 12:44:53, 37] Deleted (Deleted). LWT = 01.01.1601 4:00:00 (504911376000000000) 

// appearing deleted file
//[07.12.2013 12:51:49, 35] Renamed (Renamed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:51:49, 35] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:51:49, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 

// rename our file -> another
//[07.12.2013 12:49:57, 20] Renamed (Renamed). LWT = 01.01.1601 4:00:00 (504911376000000000) 

// rename another file -> our
//[07.12.2013 12:50:55, 20] Renamed (Renamed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:50:55, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 
//[07.12.2013 12:50:55, 20] Changed (Changed). LWT = 07.12.2013 12:49:45 (635220173857035390) 

        let invalidate (args: FileSystemEventArgs) =
            let curr = getLastWrite()
            log (sprintf "%A. Last = %A, Curr = %A" args.ChangeType !lastWrite curr)

            let needInvalidate = 
                match !lastWrite, curr with
                | Some _, None -> true
                | None, Some _ -> true
                | Some last, Some curr -> last <> curr
                | None, None -> false
            
            if needInvalidate then
                lastWrite := curr
                log "invalidate"
                this.Invalidate()

        w.Renamed.Add invalidate
        w.Deleted.Add invalidate
        w.EnableRaisingEvents <- true
        watcher <- Some w

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
            log "disposing"
            disposeWatcher()

[<TypeProviderAssembly>]
do ()
