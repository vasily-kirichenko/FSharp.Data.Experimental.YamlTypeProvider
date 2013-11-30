#r @".\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @".\bin\Debug\FSharp.Data.YamlTypeProvider.Runtime.dll"

open System
open FSharp.Data.Experimental
open System.IO

[<Literal>]
let yaml = """
K1:
    K11:
        - L11
        - L12
    K12: V12
K2:
    K21: true
    K23:
        K231: V231
        K232: V232
    K22: V22
    K24: 00:01:00
"""

type T = Yaml<YamlText=yaml>

let t = T()
t.K1.K11 <- [|"dd"|]
t.K1.K11.[0]
t.K1.K11 <- [|"new!"; "and one another"; "and even one more..."|]
t.K2.K23.K232
t.K2.K23.K232 <- "new!!!"
t.K1.K12
t.K2.K22
t.ToString()

