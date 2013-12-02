#r @"..\..\lib\SharpYaml\SharpYaml.dll"

open System
open System.IO
open System.Reflection
open System.Collections.Generic

let yaml = """
K2:
    K21: true
    K23:
        K231: V231
        K232: V232
    K22: V22
    K24: 00:01:00
    K25: 0,12
K1:
    K11:
    - x
    - y
    - z
    K12: V12
"""

//let res = YamlParser.parse yaml