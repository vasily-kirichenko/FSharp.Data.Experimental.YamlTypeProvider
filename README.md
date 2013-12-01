FSharp.Data.Experimental.YamlTypeProvider
=========================================

It's a Yaml F# Type Provider. 

It's generated, hence the types can be used from any .NET language, not only from F# code.

It can produce mutable properties for Scalars (leafs), which means the object tree can be loaded, modified and saved into the original file or a stream as Yaml text. Adding new properties is not supported, however lists can be replaced with new ones atomically. This is intentionally, see below. 

The main initial purpose for this is to be used as part of a statically typed application configuration system which would have a single master source of configuration structure - a Yaml file. Than any F#/C# project in a solution will able to use the generated read-only object graph.

When you push a system into production, you can modify the configs with scripts written in F# in safe, statically checked way with full intellisence.

Examples
========
Using configuration from a C# code
----------------------------------
Let's create a F# project, add ```Settings.yaml``` file into it:
```
Mail:
  Smtp:
    Host: smtp.sample.com
    Port: 443
    User: user1
    Password: pass1
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
  ConnectionString: Data Source=server1;Initial Catalog=Database1;Integrated Security=SSPI;
  NumberOfDeadlockRepeats: 5
  DefaultTimeout: 00:05:00
```
Declare a Yaml type and point it to the file above:
```fsharp
namespace Settings
open FSharp.Data.Experimental
type Settings = Yaml<"Settings.yaml">
```
Compile it. Now we have assembly Settings.dll containing generated types with the default values set in thier construcors.
Let's test it in a C# project. Create a Console Application, add reference to FSharp.Data.Experimental.YamlTypeProvider.dll and Setting project. 
First, try to create an instance of our generated Settings type and check that all the values are there:
```csharp
static void Main(string[] args)
{
    var settings = new Settings.Settings();
    Console.WriteLine(string.Format("Default settings:\n{0}", settings));
    Console.ReadLine();
}
```
It should outputs this:
```
Default settings:
Mail:
  Smtp:
    Host: smtp.sample.com
    Port: 443
    User: user1
    Password: pass1
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
  ConnectionString: Data Source=server1;Initial Catalog=Database1;Integrated Security=SSPI;
  NumberOfDeadlockRepeats: 5
  DefaultTimeout: 00:05:00
```
It's not very interesting so far, as the main purpose of any settings is to be changed in runtime. 
Add the following ```RuntimeSettings.yaml``` into the C# console project:
```
Mail:
  Smtp:
    Host: smtp2.sample.com
    Port: 444
    User: user11
    Password: pass11
  Pop3:
    Host: pop32.sample.com
    Port: 332
    User: user2
    Password: pass2
    CheckPeriod: 00:02:00
  ErrorNotificationRecipients:
    - user11@sample.com
    - user22@sample.com
    - new_user@sample.com
DB:
  ConnectionString: Data Source=server2;Initial Catalog=Database1;Integrated Security=SSPI;
  NumberOfDeadlockRepeats: 5
  DefaultTimeout: 00:10:00
```
We changed almost every setting here. Update our default setting with this file:
```csharp
... as before
settings.Load(@"..\..\RuntimeSettings.yaml");
Console.WriteLine(string.Format("Loaded settings:\n{0}", settings));
Console.ReadLine();
```
The output should be:
```
Loaded settings:
Mail:
  Smtp:
    Host: smtp2.sample.com
    Port: 444
    User: user11
    Password: pass11
  Pop3:
    Host: pop32.sample.com
    Port: 332
    User: user2
    Password: pass2
    CheckPeriod: 00:02:00
  ErrorNotificationRecipients:
  - user11@sample.com
  - user22@sample.com
  - new_user@sample.com
DB:
  ConnectionString: Data Source=server2;Initial Catalog=Database1;Integrated Security=SSPI;
  NumberOfDeadlockRepeats: 5
  DefaultTimeout: 00:10:00
```
Great! Values have been updated properly, new user has been added into ```ErrorNotificationRecipients``` list.

In the next example we'll change configs in statically typed manner, via F# scripts.
