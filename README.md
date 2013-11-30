FSharp.Data.Experimental.YamlTypeProvider
=========================================

It's a Yaml F# Type Provider. 

It's generated, hence the types can be used from any .NET language, not only from F# code.

It can produce mutable properties for Scalars (leafs), which means the object tree can be loaded, modified and saved into the original file or a stream as Yaml text. Adding new properties is not supported, however lists can be replaced with new ones atomically. This is intentionally, see below. 

The main initial purpose for this is to be used as part of a statically typed application configuration system which would have a single master source of configuration structure - a Yaml file. Than any F#/C# project in a solution will able to use the generated read-only object graph.

When you push a system into production, you can modify the configs with scripts written in F# in safe, statically checked way with full intellisence.
