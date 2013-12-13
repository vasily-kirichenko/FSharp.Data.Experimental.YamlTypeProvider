#r @"l:\git\SharpYaml\SharpYaml\bin\Debug\SharpYaml.dll"
#r @"..\src\FSharp.Data.YamlTypeProvider\bin\Debug\FSharp.Data.YamlTypeProvider.dll"
#r @"Settings\bin\Debug\Settings.dll"

let settings = Settings.Settings()
settings.LoadAndWatch @"l:\git\FSharp.Data.Experimental.YamlTypeProvider\examples\CSharpExample\RuntimeSettings.yaml"
settings.Changed.Add (fun _ -> printfn "Changed!\n%O" settings)
//settings.Mail.Smtp.Ssl <- false
settings.ToString()


