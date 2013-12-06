#r @"..\..\lib\SharpYaml\SharpYaml.dll"
#load "YamlParser.fs"

open System
open System.IO
open System.Reflection
open System.Collections.Generic
open FSharp.Data.Experimental

let watcher = new FileSystemWatcher(Filter = "Settings.yaml", Path = @"d:\git\FSharp.Data.Experimental.YamlTypeProvider\examples\Settings")

watcher.Changed
|> Event.merge watcher.Created
|> Event.merge watcher.Deleted
|> Event.add (fun e -> Console.WriteLine (sprintf "\n%s %A" e.Name e.ChangeType))

watcher.EnableRaisingEvents <- true
watcher.Dispose()

let yaml = """
Root:
  -
    Name: App1
    Count: 1
  -
    Name: App2
    Count: 2
"""

let res = YamlParser.parse yaml

//let yaml = """
//K2:
//    K21: true
//    K23:
//        K231: V231
//        K232: V232
//    K22: V22
//    K24: 00:01:00
//    K25: 0,12
//K1:
//    K11:
//    - x
//    - y
//    - z
//    K12: V12
//"""