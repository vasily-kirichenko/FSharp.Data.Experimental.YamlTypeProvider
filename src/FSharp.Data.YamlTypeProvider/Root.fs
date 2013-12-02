namespace FSharp.Data.Experimental

open System.IO
open SharpYaml.Serialization

type Root () = 
    let serializer = Serializer(SerializerSettings(EmitDefaultValues=true, EmitTags=false, SortKeyForMapping=false))
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

