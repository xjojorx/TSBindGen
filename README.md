# TSBindGen

Reflection-based generation of typescript definitions from C# types.

## Description

This is a typescript generator in order to ease the usage of your already defined C# types on a TypeScript client. It is reflection based in order to be both configurable from within the code, and to be able to resolve dependent types.

The library is composed of a single static class, at any point of an application, call into `TSGenerator.GenerateTypes` to generate the resulting type declarations. The function allows for passing either a list of types or an `Assembly` plus a list of namespaces, that way you can select what types to generate and which ones to ignore however it is more convenient for each codebase. It is possible to get a string in return or to provide a `TextWriter` as the last argument, which can be used to write directly to a file.

Example:
```c#
string path = "../frontend/src/types/generated.ts";
using (var sink = new StringWriter(path)){
    TSGenerator.GenerateTypes(Assembly.GetExecutingAssembly(), ["MyApp.Models.API", "MyApp.Models.DTOs"], sink);
}
```

It is also possible to get the result as string:
```c#
string tsTypes = TSGenerator.GenerateTypes(typeof(MyType1));
```

Since it is reflection-based, it should be compatible with any C# type that can be inspected at runtime. There are some edge cases for which `any` or `unknown` are used since the result could not be determined. The expectation is that on those cases you can still use it as a base for a full-fledged type, use it as a startint point for a client-side transformation, or just go with the less type-safe option of leaving it as-is.

## Installation

The recommendation is to download and vendor the file `src/TSBindGen/TSGenerator.cs` into your own codebase. Since it is a small single file and simple enough to be modified, there is no need for downloading a whole nuget package, plus it is easier to adjust whatever is needed for your specific use-case that way (for example in order to fix an edge-case without needing to either alter your model or generalizing it so it can be merged).

If you still prefer or need a nuget package, there should be one available soon.

```
dotnet add TSBindGen
```

## Why another TypeScript Generator

There are many tools for generation TypeScript definitions for your C# projects, but usually they either parse the source code and translate it, or depend on adding properties or other tags through your whole program.
While they tend to work fine, I find it either lacking of context or unnecesarily complex.

The idea is that, since each project and team is different, the bindings between your frontend and backend should live in the code itself, so it can be configured and adapted as the project evolves. You can run it at application startup on development, run either generation or the app depending on a parameter, try to hook it at compile time. It is a just simple no-dependencies C# function that you can run on any context that suits your project.

## License

TSBindGen is licensed under an unmodified zlib/libpng license, which is an OSI-certified, BSD-like license that allows the usage in close-source and commercial projects. Attribution is appreciated but not required. Check [LICENSE](LICENSE) for more details.
