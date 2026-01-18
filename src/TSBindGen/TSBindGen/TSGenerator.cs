/********************************************************************************
 * LICENSE: zlib/libpng
 *
 * Copyright (c) 2026 Juan Diez Liste
 *
 * This software is provided ‘as-is’, without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 * claim that you wrote the original software. If you use this software
 * in a product, an acknowledgment in the product documentation would be
 * appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not be
 * misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 * distribution.
 *
 *******************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TSBindGen;

/// <summary>
/// Module-like function container for TSBindGen.
/// This is the whole library, with multiple entrypoints that do the same thing with different input parameters.
/// Any program using this generator should usually contain just a single call to the entry that's prefered by the developer.
/// </summary>
/// <remarks>
/// Feel free to vendor this class and add your own edge-cases, even if you don't intend to do a fully generic solution and upstream it.
/// </remarks>
public static class TSGenerator
{
    #region  public API
    /// <summary>
    /// Generates a typescript definition string for a list of types.
    /// The result includes the requested types, along with all the required dependencies in order to be able to use the type in TS.
    /// </summary>
    /// <seealso cref="GenerateTypes(IEnumerable&lt;Type&gt;, TextWriter)"/>
    /// <param name="requestedTypes">List of types to explore and generate</param>
    public static string GenerateTypes(IEnumerable<Type> requestedTypes)
    {
        using var sink = new StringWriter();
        GenerateTypes(requestedTypes, sink);
        return sink.ToString();
    }
    
    /// <summary>
    /// Generates a typescript definition string for a list of types.
    /// The result includes the requested types, along with all the required dependencies in order to be able to use the type in TS.
    /// </summary>
    /// <seealso cref="GenerateTypes(Assembly, IList&lt;string&gt;, TextWriter)"/>
    /// <param name="asm">Assembly to look for the namespaces in</param>
    /// <param name="namespaces">List of namespace <b>prefixes</b> to explore</param>
    public static string GenerateTypes(Assembly asm, IList<string> namespaces)
    {
        using var sink = new StringWriter();
        GenerateTypes(asm, namespaces, sink);
        return sink.ToString();
    }


    /// <summary>
    /// <para>
    /// Generates a typescript definition string for a list of types, and writes to a TextWriter.
    /// The result includes the requested types, along with all the required dependencies in order to be able to use the type in TS.
    /// </para>
    /// <example>
    /// To write the result to a file:
    /// <code>
    /// var tempFile = Path.GetTempFileName();
    /// using (var sink = new StreamWriter(tempFile))
    /// {
    ///     TSGenerator.GenerateTypes(asm, namespaces, sink);
    ///     // flushing may not be needed if the stream makes sure of it when disposing, but I like to flush when I work with files just in case
    ///     sink.Flush();
    /// }
    /// </code>
    /// </example>
    /// </summary>
    /// <seealso cref="GenerateTypes(Assembly, IList&lt;string&gt;, TextWriter)"/>
    /// <param name="asm">Assembly to look for the namespaces in</param>
    /// <param name="namespaces">List of namespace <b>prefixes</b> to explore</param>
    /// <param name="sink">TextWriter to write the result to</param>
    public static void GenerateTypes(Assembly asm, IList<string> namespaces, TextWriter sink)
    {
        var requestedTypes = asm.DefinedTypes
            .Where(dt => dt.IsPublic)
            .Where(dt => !string.IsNullOrWhiteSpace(dt.Namespace)
                         && namespaces.Any(n => dt.Namespace.StartsWith(n))
            ).ToList();


        GenerateTypes(requestedTypes, sink);
    }
    
    /// <summary>
    /// <para>
    /// Generates a typescript definition string for a list of types, and writes to a TextWriter.
    /// The result includes the requested types, along with all the required dependencies in order to be able to use the type in TS.
    /// </para>
    /// <example>
    /// To write the result to a file:
    /// <code>
    /// var tempFile = Path.GetTempFileName();
    /// using (var sink = new StreamWriter(tempFile))
    /// {
    ///     TSGenerator.GenerateTypes(asm, namespaces, sink);
    ///     // flushing may not be needed if the stream makes sure of it when disposing, but I like to flush when I work with files just in case
    ///     sink.Flush();
    /// }
    /// </code>
    /// </example>
    /// </summary>
    /// <seealso cref="GenerateTypes(IEnumerable&lt;Type&gt;)"/>
    /// <param name="requestedTypes">List of types to explore and generate</param>
    /// <param name="sink">TextWriter to write the result to</param>
    public static void GenerateTypes(IEnumerable<Type> requestedTypes, TextWriter sink)
    {
        
        var q = new Queue<Type>(requestedTypes);
        var allTypes = new HashSet<Type>(capacity: 1024);
        var explored = new List<string>(capacity: 1024);

        while (q.TryDequeue(out var type))
        {
            if (explored.Contains(GetTypeId(type)))
            {
                continue;
            }
            allTypes.Add(type);
            
            //Add dependencies to queue
            var props = type.GetProperties();
            foreach (var prop in props)
            {
                var propType = prop.PropertyType;

                if (propType.IsGenericTypeParameter)
                {
                    continue;
                }

                // bool isCollection = propType.IsAssignableTo(typeof(IEnumerable));
                if (propType.IsGenericType)
                {
                    var genTypes = propType.GenericTypeArguments;
                    foreach (var genType in genTypes)
                    {
                        if (ShouldTranslateType(genType))
                        {
                            q.Enqueue(genType);
                        }
                    }
                }
                

                if (!ShouldTranslateType(propType))
                {
                    //do not add to the types to generate, it will be translated via default shcema
                    continue;
                }

                // allTypes.Add(propType);
                if (!explored.Contains(GetTypeId(propType)))
                {
                    q.Enqueue(propType);
                }
            }

            explored.Add(GetTypeId(type));
        }

        //Do the thing
        foreach (var t in allTypes)
        {
            var ti = t.GetTypeInfo();
            string tName = t.Name.Split('`').First();

            sink.Write($"export interface {tName}");
            if (ti.ContainsGenericParameters)
            {
                sink.Write("<");
                bool first = false;
                foreach (var genArg in ti.GenericTypeParameters)
                {
                    if (!first)
                    {
                        sink.Write(", ");
                        first = false;
                    }
                    sink.Write(genArg.Name);
                }
                
                sink.Write(">");
            }
            sink.WriteLine(" {");
            
            var props = t.GetProperties();
            var fields = t.GetFields();
            var members = props.Select(p => new { name = GetPropertyName(p), type = p.PropertyType })
                .Union(fields.Select(f => new { name = f.Name, type = f.FieldType }));

            foreach (var prop in members)
            {
                string name = prop.name;
                string type = GetTsVersion(prop.type);
                
                //TODO: We should probably emit some kind of information about unknown types being generated for the user to do their own tuning. Or allow for some hooking for handling unknowns inline, maybe receiving a callback
                Debug.Assert(type != "unknown");
                Debug.Assert(type != "unknown[]");
                sink.WriteLine($"\t{name}: {type};");
            }
            
            sink.WriteLine("}\n");
        }
        sink.Flush();
    }
    #endregion

    #region private functions 
    private static string GetPropertyName(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();

        return attr?.Name ?? propertyInfo.Name;
    }

    private static string GetTsVersion(Type csType)
    {
        string type;
        
        if (ShouldTranslateType(csType))
        {
            return csType.Name;
        }

        //NOTE: Tried using ICollection, but it seems ICollection<T> does not implement ICollection. I guess generic collections are not collections
        bool isCollection = typeof(IEnumerable).IsAssignableFrom(csType);
        if (!isCollection)
        {
            return GetJsType(csType);
        }

        // strings are enumerables, tee-hee
        if (csType == typeof(string))
        {
            
            return "string";
        }

        if (!csType.IsGenericType)
        {
            var itemType = csType.GetElementType();
            if (itemType is null)
            {
                //not an array, no idea what else can be here. Assume a truly non-generic collection, thus any[]
                return "any[]";
            }
            
            return $"{GetTsVersion(itemType)}[]";
            
        }

        var ofType = csType.GenericTypeArguments;
        
        if (ofType.Length == 1)
        {
            var collType = GetTsVersion(ofType[0]);
            type = $"{collType}[]";
            return type;
        }
        

        //it may be a dictionary
        if (csType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = ofType[0];
            var valType = ofType[1];
            var keyStr = GetTsVersion(keyType);
            if (keyStr.Contains('|'))
            {
                keyStr = keyStr.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(tn => tn != "undefined" && tn != "null")
                    .First();
            }
            type = $"{{ [key: {keyStr}]: {GetTsVersion(valType)} }}";
        }
        else
        {
            //idk what to do with this, some programmer input is needed at some point anyway
            type = "unknown[]";
        }

        return type;
    }
    

    private static string GetTypeId(Type type) => type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
    private static bool ShouldTranslateType(Type type) => !(type.IsPrimitive ||  (type.AssemblyQualifiedName?.StartsWith("System") ?? false));

    private static string GetJsType(Type type)
    {
        try
        {

            //translate via json schema
            var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            opt.MaxDepth = 255;
            opt.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            opt.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
            var schema = opt.GetJsonSchemaAsNode(type);


            var schemaType = schema["type"];
            var typeKind = schemaType?.GetValueKind();
            if(typeKind == JsonValueKind.String)
            {
                var schemaTypeStr = schemaType!.GetValue<string>();
                return ResolveJsType(schemaTypeStr);
            } else if (typeKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                var vals = schemaType!.AsArray();
                bool first = true;
                foreach (var val in vals)
                {
                    string t = val!.GetValue<string>();
                    if (!first)
                    {
                        sb.Append($" | ");
                    }

                    var jsType = ResolveJsType(t);
                    Debug.Assert(jsType != "unknown", "jsType != 'unknown'");
                    Debug.Assert(jsType != "unknown[]", "unknown array");
                    sb.Append(jsType);
                    
                    if (t == "null")
                    {
                        //add also undefined to be sure
                        sb.Append(" | undefined");
                    }

                    first = false;
                }
                return sb.ToString();
            }

            Debug.Assert(false, "???");

            return "unknown";
        }
        catch(Exception ex)
        {
            Debug.Assert(false, ex.Message);
            Console.Error.WriteLine(ex);
            return "unknown";
        }
    }

    private static string ResolveJsType(string schemaTypeStr)
    {
        return schemaTypeStr switch
        {
            "integer" => "number",
            "float" or "double" or "decimal" => "number",
            "number" => "number",
            "string" => "string",
            "boolean" or "true" or "false" => "boolean",
            "null" => "null",
            "array" => "unknown[]",
            _ => "unknown"
        };
    }
    #endregion
}



