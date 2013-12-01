#r @"Settings\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open System

let settings = Settings.Settings()

settings.Mail.Pop3.Port <- 400
settings.DB.ConnectionString <- "new connection string"
settings.DB.DefaultTimeout <- TimeSpan.FromMinutes 30.

settings.Save (__SOURCE_DIRECTORY__ + @"\ChangedSettings.yaml")

