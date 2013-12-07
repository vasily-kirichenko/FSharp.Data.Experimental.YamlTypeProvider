namespace Settings

open FSharp.Data.Experimental

type Settings = Yaml<"Settings.yaml">

module T = 
    let settings = Settings()
    let x = settings.RelativePath