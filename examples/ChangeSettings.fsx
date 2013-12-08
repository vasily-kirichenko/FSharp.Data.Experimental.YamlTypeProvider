#r @"..\src\FSharp.Data.YamlTypeProvider\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

open System

let s = Settings.Settings()

s.DB.Changed.Add(fun _ -> printfn "DB changed!")
s.Mail.Changed.Add(fun _ -> printfn "Mail changed!")

s.LoadText """
Mail:
  Smtp:
    Host: smtp.sample.com
    Port: 443
    User: user1
    Password: pass1
    Ssl: true
  Pop3:
    Host: pop3.sample.com
    Port: 331
    User: user2
    Password: pass2
    CheckPeriod: 00:01:00
  ErrorNotificationRecipients:
    - user1@sample.com
    - user2@sample.com
DB:
  ConnectionString: Data Source=server1;Initial Catalog=Database1;Integrated Security=SSPI 1;
  NumberOfDeadlockRepeats: 5
  DefaultTimeout: 00:05:00
Dashboard: http://sample.domain.com/dashboard.xml
Collaborations: [ "http://sample.domain.com/dashboard.xml" ]
SharedFile: \\server\dir\file.txt
LocalFile: c:\dir\file.txt
RelativePath: ..\AvpBasesUpdater\AvpBases\KdcProduction
""" |> ignore

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
