#r @"..\src\FSharp.Data.YamlTypeProvider\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open System

let settings = Settings.Settings()
let pop3host = settings.Mail.Pop3.Host
settings.Mail.Pop3.Port <- 400
settings.DB.ConnectionString <- "new connection string"
settings.DB.DefaultTimeout <- TimeSpan.FromMinutes 30.
settings.Dashboard

settings.Save (__SOURCE_DIRECTORY__ + @"\ChangedSettings.yaml")
