#r @"Settings\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open Settings

let settings = Settings()
settings.DB.ConnectionString <- "new connection string"
settings.Save (__SOURCE_DIRECTORY__ + @"ChangedSettings.yaml")

