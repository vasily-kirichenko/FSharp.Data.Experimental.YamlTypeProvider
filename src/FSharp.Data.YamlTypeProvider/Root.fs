namespace FSharp.Data.Experimental

open System
open System.IO
open SharpYaml.Serialization
open SharpYaml.Serialization.Serializers

type Root () = 
    let serializer = 
        let settings = SerializerSettings(EmitDefaultValues=true, EmitTags=false, SortKeyForMapping=false)
        settings.RegisterSerializer(typeof<System.Uri>, 
                                    { new ScalarSerializerBase() with
                                        member x.ConvertFrom (ctx, scalar) = 
                                                match System.Uri.TryCreate(scalar.Value, UriKind.Absolute) with
                                                | true, uri -> box uri
                                                | _ -> null
                                        member x.ConvertTo ctx = 
                                                match ctx.Instance with
                                                | :? Uri as uri ->  uri.OriginalString
                                                | _ -> "" })
        Serializer(settings)
    
    /// Load Yaml text and update itself with it.
    member x.LoadText (yamlText: string) =
        YamlParser.parse yamlText |> YamlParser.update x
    /// Load Yaml from a TextReader and update itself with it.
    member x.Load (reader: TextReader) =
        reader.ReadToEnd() |> YamlParser.parse |> YamlParser.update x
    /// Load Yaml from a file and update itself with it.
    member x.Load (filePath: string) =
        File.ReadAllText filePath |> YamlParser.parse |> YamlParser.update x
    /// Saves content into a stream.
    member x.Save (stream: Stream) =
        use writer = new StreamWriter(stream)
        x.Save writer
    /// Saves content into a TestWriter.
    member x.Save (writer: TextWriter) =
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

