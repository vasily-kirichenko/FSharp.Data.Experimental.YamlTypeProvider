module FSharp.Data.Experimental.YamlParser

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
        | scalar -> Scalar (Scalar.Parse (scalar.ToString()))

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

    let rec update (target: obj) name (updater: Node) =
        match name, updater with
        | Some name, Scalar (_ as x) -> updateScalar target name x
        | _, Map m -> updateMap target name m
        | Some name, List l -> updateList target name l
        | None, _ -> failwithf "Only Maps are allowed at the root level."
    
    and updateScalar (target: obj) name (node: Scalar) =
        let field = getField target ("_" + name)
        if field.FieldType <> node.UnderlyingType then 
            failwithf "Cannot assign value of type %s to field of type %s." node.UnderlyingType.Name field.FieldType.Name
        field.SetValue(target, getBoxedNodeValue node)

    and updateList (target: obj) name (updaters: Node list) =
        let updaters = updaters |> List.choose (function Scalar x -> Some x | _ -> None)

        let elementType = 
            match updaters |> Seq.groupBy (fun n -> n.UnderlyingType) |> Seq.map fst |> Seq.toList with
            | [ty] -> ty
            | types -> failwithf "List cannot contain elements of heterohenius types (attempt to mix types: %A)." types

        let fieldType = typedefof<ResizeArray<_>>.MakeGenericType elementType

        let field = getField target ("_" + name)
        if field.FieldType <> fieldType then failwithf "Cannot assign %O to %O." fieldType.Name field.FieldType.Name
        let list = Activator.CreateInstance(fieldType)
        let addMethod = fieldType.GetMethod("Add", [|elementType|])
        updaters |> List.iter (fun x -> addMethod.Invoke(list, [|getBoxedNodeValue x|]) |> ignore)
        field.SetValue(target, list)

    and updateMap (target: obj) name (updaters: (string * Node) list) =
        let target = 
            match name with
            | Some name ->
                let ty = target.GetType()
                let mapProp = ty.GetProperty name
                if mapProp = null then failwithf "Type %s does not contain %s property." ty.Name name
                mapProp.GetValue target
            | None -> target

        updaters |> List.iter (fun (name, node) -> update target (Some name) node)

    update target None updater
    target


