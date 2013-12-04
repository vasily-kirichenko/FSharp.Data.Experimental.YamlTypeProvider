#r @"..\src\FSharp.Data.YamlTypeProvider\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open System

let s = Settings.Settings()
let pop3host = s.Mail.Pop3.Host
s.Mail.Pop3.Port <- 400
s.DB.ConnectionString <- "new connection string"
s.DB.DefaultTimeout <- TimeSpan.FromMinutes 30.
s.Dashboard
s.Dashboard <- Uri("ftp://new.com/1.html")
s.Mail.ErrorNotificationRecipients <- [|"s1"|]
s.Collaborations <- [| Uri ("http://1.com") |]
s.RelativePath <- "..\AvpBasesUpdater\AvpBases\KdcProduction"

s.Save (__SOURCE_DIRECTORY__ + @"\ChangedSettings.yaml")
