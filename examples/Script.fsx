//#r @"..\packages\SharpYaml.1.1.0\lib\SharpYaml.dll"
#r @"d:\git\SharpYaml\SharpYaml\bin\Debug\SharpYaml.dll"
#r @"..\src\FSharp.Data.YamlTypeProvider\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open SharpYaml.Serialization

let settings = Settings.Settings()
settings.Mail.Smtp.Ssl <- false
settings.ToString()

let serSettings = SerializerSettings(EmitDefaultValues=true, EmitTags=false, SortKeyForMapping=false)
let serializer = Serializer(serSettings)
let s = serializer.Serialize settings
s

[<CLIMutable>] type T = { Z_1: int; A_2: int; P_3: int }

let t = { Z_1 = 1; A_2 = 2; P_3 = 3 }
let tYaml = serializer.Serialize t
tYaml