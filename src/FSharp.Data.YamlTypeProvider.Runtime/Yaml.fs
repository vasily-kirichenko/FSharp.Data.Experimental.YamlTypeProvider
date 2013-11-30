namespace FSharp.Data.Experimental

open System
    open System.IO
    open YamlDotNet.Core
    open YamlDotNet.RepresentationModel

module Yaml =
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

type Root () = 
    /// Saves content into a stream.
    member x.Save (stream: Stream) =
        use writer = new StreamWriter(stream)
        x.Save writer
    /// Saves content into a TestWriter.
    member x.Save (writer: TextWriter) =
        let serializer = YamlDotNet.RepresentationModel.Serialization.Serializer()
        serializer.Serialize(writer, x)
    /// Saves content into a file.
    member x.Save (filePath: string) =
        use file = new FileStream(filePath, FileMode.Create)
        x.Save file
    /// Returns content as Yaml text.
    override x.ToString() = 
        use writer = new StringWriter()
        x.Save writer
        writer.ToString()


