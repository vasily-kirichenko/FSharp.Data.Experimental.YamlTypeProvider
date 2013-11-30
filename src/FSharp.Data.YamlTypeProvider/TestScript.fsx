#r @".\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"..\..\packages\YamlDotNet.Core.2.2.0\lib\net35\YamlDotNet.Core.dll"
#r @"..\..\packages\YamlDotNet.RepresentationModel.2.2.0\lib\net35\YamlDotNet.RepresentationModel.dll"

open System
open FSharp.Data.Experimental.YamlTypeProvider
open System.IO
open YamlDotNet.Core
open YamlDotNet.RepresentationModel
open YamlDotNet.RepresentationModel.Serialization

[<Literal>]
let yaml = """
K1:
    K11:
        - x
        - y
    K12: V12
K2:
    K21: true
    K23:
        K231: V231
        K232: V232
    K22: V22"""

type T = Yaml<YamlText=yaml>

let t = T()
t.K1.K11 <- [|"dd"|]
t.K1.K11.[0]
t.K1.K11 <- [|"new!"; "and one another"; "and even one more..."|]
t.K2.K23.K232
t.K2.K23.K232 <- "new!!!"
t.K1.K12
t.K2.K22
t.K2.K33
t.K2.K33 <- TimeSpan.FromDays 1.
t.Save()

let reader = new StreamReader(@"d:\git\FSharp.Data.YamlTypeProvider\Test.yml")
let deserializer = new Deserializer()
let t1 = deserializer.Deserialize<T>(reader)
t1.K1
t1.K2.K33
