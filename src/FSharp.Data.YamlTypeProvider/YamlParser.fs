module FSharp.Configuration.YamlParser

open System
open System.IO
open System.Reflection
open System.Collections.Generic
open SharpYaml
open SharpYaml.Serialization
open SharpYaml.Serialization.Serializers

type Scalar =
    | Int of int
    | String of string
    | TimeSpan of TimeSpan
    | Bool of bool
    | Uri of Uri
    static member Parse (value: string) =
        let isUri (value: string) = 
            ["http";"https";"ftp";"ftps";"sftp";"amqp"] 
            |> List.exists (fun x -> value.Trim().StartsWith(x + ":", StringComparison.InvariantCultureIgnoreCase))

        match bool.TryParse value with
        | true, x -> Bool x
        | _ ->
            match Int32.TryParse value with
            | true, x -> Int x
            | _ -> match TimeSpan.TryParse value with
                    | true, x -> TimeSpan x
                    | _ -> 
                        match isUri value, Uri.TryCreate(value, UriKind.Absolute) with
                        | true, (true, x) -> Uri x 
                        | _ -> String value
    member x.UnderlyingType = 
        match x with
        | Int x -> x.GetType()
        | String x -> x.GetType()
        | Bool x -> x.GetType()
        | TimeSpan x -> x.GetType()
        | Uri x -> x.GetType()
        
type Node =
    | Scalar of Scalar
    | List of Node list
    | Map of (string * Node) list

let parse : (string -> Node) =
    let rec loop (n: obj) =
        match n with
        | :? List<obj> as l -> Node.List (l |> Seq.map loop |> Seq.toList)
        | :? Dictionary<obj,obj> as m -> 
            Map (m |> Seq.choose (fun p -> 
                match p.Key with
                | :? string as key -> Some (key, loop p.Value)
                | _ -> None) |> Seq.toList)
        | scalar ->
            let scalar = if scalar = null then "" else scalar.ToString()
            Scalar (Scalar.Parse scalar)

    let settings = SerializerSettings(EmitDefaultValues=true, EmitTags=false, SortKeyForMapping=false)
    let serializer = Serializer(settings)
    fun text -> serializer.Deserialize(fromText=text) |> loop

let update (target: 'a) (updater: Node) =
    let getBoxedNodeValue =
        function
        | Int x -> box x
        | String x -> box x
        | TimeSpan x -> box x
        | Bool x -> box x
        | Uri x -> box x

    let getField o name =
        let ty = o.GetType()
        let field = ty.GetField(name, BindingFlags.Instance ||| BindingFlags.NonPublic)
        if field = null then failwithf "Field %s was not found in %s." name ty.Name
        field

    let getChangedDelegate x = 
        x.GetType().GetField("_changed", BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue x :?> MulticastDelegate 

    let rec update (target: obj) name (updater: Node) =
        match name, updater with
        | _, Scalar (_ as x) -> updateScalar target name x 
        | _, Map m -> updateMap target name m
        | Some name, List l -> updateList target name l
        | None, _ -> failwithf "Only Maps are allowed at the root level."
    
    and updateScalar (target: obj) name (node: Scalar) =
        match name with
        | Some name -> 
            let field = getField target ("_" + name)
        
            if field.FieldType <> node.UnderlyingType then 
                failwithf "Cannot assign value of type %s to field of %s: %s." node.UnderlyingType.Name name field.FieldType.Name

            let oldValue = field.GetValue(target)
            let newValue = getBoxedNodeValue node
        
            if oldValue <> newValue then
                field.SetValue(target, newValue)
                [getChangedDelegate target]
            else []
        | _ -> []

    and updateList (target: obj) name (updaters: Node list) =
        let updaters = updaters |> List.choose (function Scalar x -> Some x | _ -> None)

        let field = getField target ("_" + name)
        
        let fieldType = 
            match updaters |> Seq.groupBy (fun n -> n.UnderlyingType) |> Seq.map fst |> Seq.toList with
            | [] -> field.FieldType
            | [ty] -> typedefof<ResizeArray<_>>.MakeGenericType ty
            | types -> failwithf "List cannot contain elements of heterohenius types (attempt to mix types: %A)." types

        if field.FieldType <> fieldType then failwithf "Cannot assign %O to %O." fieldType.Name field.FieldType.Name

        let sort (xs: obj seq) = 
            xs 
            |> Seq.sortBy (function
               | :? Uri as uri -> uri.OriginalString :> IComparable
               | :? IComparable as x -> x
               | x -> failwithf "%A is not comparable, so it cannot be included into a list."  x)
            |> Seq.toList

        let oldValues = field.GetValue(target) :?> Collections.IEnumerable |> Seq.cast<obj> |> sort
        let newValues = updaters |> List.map getBoxedNodeValue |> sort

        if oldValues <> newValues then
            let list = Activator.CreateInstance fieldType
            let addMethod = fieldType.GetMethod("Add", [|fieldType.GetGenericArguments().[0]|])
            updaters |> List.iter (fun x -> addMethod.Invoke(list, [|getBoxedNodeValue x|]) |> ignore)
            field.SetValue(target, list)
            [getChangedDelegate target]
        else []

    and updateMap (target: obj) name (updaters: (string * Node) list) =
        let target = 
            match name with
            | Some name ->
                let ty = target.GetType()
                let mapProp = ty.GetProperty name
                if mapProp = null then failwithf "Type %s does not contain %s property." ty.Name name
                mapProp.GetValue target
            | None -> target

        match updaters |> List.collect (fun (name, node) -> update target (Some name) node) with
        | [] -> []
        | events -> getChangedDelegate target :: events // if any child is raising the event, we also do (pull it up the hierarchy)

    update target None updater
    |> Seq.filter ((<>) null)
    |> Seq.collect (fun x -> x.GetInvocationList())
    |> Seq.distinct
    //|> fun x -> printfn "Updated. %d events to raise: %A" (Seq.length x) x; Seq.toList x
    |> Seq.iter (fun h -> h.Method.Invoke(h.Target, [|box target; EventArgs.Empty|]) |> ignore)
