#r @"..\packages\SharpYaml.1.1.0\lib\SharpYaml.dll"
#r @"Settings\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open System
open SharpYaml
open SharpYaml.Serialization

let settings = Settings.Settings()
settings.Mail.Smtp.Ssl <- false
settings.ToString()

let st = SerializerSettings(EmitDefaultValues=true)
st.EmitAlias <- false
st.SpecialCollectionMember <- ""
let sr = Serializer(st)
let s = sr.Serialize settings
s