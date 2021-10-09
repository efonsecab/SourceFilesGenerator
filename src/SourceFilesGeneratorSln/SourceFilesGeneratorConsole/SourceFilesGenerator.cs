using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace TestGenerateAdminCode
{
    /// <summary>
    /// Searches for a DbContext in the given assemblies, in order to generate the models files as well as
    /// the Blazor Pages files
    /// </summary>
    public class SourceFilesGenerator
    {
        /// <summary>
        /// Generates models files based on a DbContext
        /// </summary>
        /// <param name="modelsDestFolder">Folder where to place generated models files</param>
        /// <param name="blazorFilesDestFolder">Folder where to place generated blazor files</param>
        /// <param name="assembliesDirectory">Folder where to search for assemblies</param>
        /// <param name="dataAccessAssemblyName">FullName for the assembly with the DbContext</param>
        public void GenerateFiles(string modelsDestFolder, string blazorFilesDestFolder, 
            string assembliesDirectory, string dataAccessAssemblyName)
        {
            List<TypeMapping> mappedTypes = new List<TypeMapping>(); ;
            var assembliesInDir = Directory.GetFiles(assembliesDirectory, "*.dll");
            foreach (var singleAssemblyFile in assembliesInDir)
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(singleAssemblyFile);
            }

            var dataAccessAssembly = AppDomain.CurrentDomain.GetAssemblies().Where(p => p.FullName == dataAccessAssemblyName).Single();
            var dbContext = dataAccessAssembly.GetTypes().Where(p => p.BaseType.Name == "DbContext").Single();
            var dbSets = dbContext.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var singleDbSet in dbSets)
            {
                var entityType = singleDbSet.PropertyType.GenericTypeArguments.Single();
                var entityProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                string csCode = GenerateModelCode(dataAccessAssembly, entityType, entityProperties);
                var blazorCreatePageCode = GenerateBlazorCreatePageCode(dataAccessAssembly, entityType, entityProperties);
                TypeMapping typeMapping = new TypeMapping()
                {
                    SourceType = entityType,
                    DestModelTypeCodeString = csCode,
                    BlazorCrud = new BlazorCrud()
                    {
                        CreatePageCodeString = blazorCreatePageCode
                    }
                };
                mappedTypes.Add(typeMapping);
            }
            foreach (var singleMappedType in mappedTypes)
            {
                string modelFileName = Path.Combine(modelsDestFolder, $"{singleMappedType.DestModelName}.cs");
                File.WriteAllText(modelFileName, singleMappedType.DestModelTypeCodeString);

                string blazorCreatePageFileName = Path.Combine(blazorFilesDestFolder, $"{singleMappedType.DestModelName}.razor");
                File.WriteAllText(blazorCreatePageFileName, singleMappedType.BlazorCrud.CreatePageCodeString);
            }
        }

        private static string GenerateModelCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties)
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendLine("using System;");
            strBuilder.AppendLine();
            strBuilder.AppendLine($"public partial class {entityType.Name}Model");
            strBuilder.AppendLine("{");
            foreach (var singlePropertyinEntity in entityProperties)
            {
                string propertyType = string.Empty;
                if (singlePropertyinEntity.PropertyType.Name.Contains("ICollection"))
                {
                    propertyType = $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.Single().Name}Model[]";
                }
                else
                if (Nullable.GetUnderlyingType(singlePropertyinEntity.PropertyType) != null)
                {
                    propertyType =
                        $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.First().Name}?";
                }
                else
                {
                    propertyType = singlePropertyinEntity.PropertyType.Name;
                }
                var propertyName = singlePropertyinEntity.Name;
                var isCustomDefinedType =
                    dataAccessAssembly.GetTypes().Any(p => p.Name == propertyType);
                if (isCustomDefinedType)
                {
                    strBuilder.AppendLine($"public {propertyType}Model {propertyName} {{get;set;}}");
                }
                else
                {
                    strBuilder.AppendLine($"public {propertyType} {propertyName} {{get;set;}}");
                }
            }
            strBuilder.AppendLine("}");
            var csCode = CSharpSyntaxTree.ParseText(strBuilder.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
            return csCode;
        }

        private static string GenerateBlazorCreatePageCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties)
        {
            CodeWriter codeWriter = new CodeWriter();
            codeWriter.WriteLine($"@page \"/Admin/{entityType.Name}/Create\"");
            codeWriter.WriteLine();
            codeWriter.WriteLine("<EditForm Model=\"@_model\">");
            List<string> variables = new List<string>();
            foreach (var singlePropertyinEntity in entityProperties)
            {
                string propertyType = string.Empty;
                if (singlePropertyinEntity.PropertyType.Name.Contains("ICollection"))
                {
                    propertyType = $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.Single().Name}Model[]";
                }
                else
                if (Nullable.GetUnderlyingType(singlePropertyinEntity.PropertyType) != null)
                {
                    propertyType =
                        $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.First().Name}?";
                }
                else
                {
                    propertyType = singlePropertyinEntity.PropertyType.Name;
                }
                var propertyName = singlePropertyinEntity.Name;
                var isCustomDefinedType =
                    dataAccessAssembly.GetTypes().Any(p => p.Name == propertyType);
                if (isCustomDefinedType)
                {
                    //strBuilder.AppendLine($"public {propertyType}Model {propertyName} {{get;set;}}");
                }
                else
                {
                    bool shouldWrite = false;
                    shouldWrite = (propertyType == "String" || propertyType == "String?" ||
                        propertyType == "Boolean" || propertyType == "Boolean?" ||
                        propertyType.StartsWith("Int"));
                    if (!shouldWrite)
                        continue;
                    variables.Add($"public {propertyType} _{propertyName} {{get;set;}}");
                    codeWriter.WriteLine("<div class=\"row\">");
                    codeWriter.WriteLine("<div class=\"col\">");
                    codeWriter.WriteLine($"<label class=\"form-label\">{propertyName}</label>");
                    if (propertyType == "String" || propertyType == "String?")
                        codeWriter.WriteLine($"<InputText class=\"form-control\" @bind-Value=\"@_model.{propertyName}\" />");
                    if (propertyType == "Boolean" || propertyType == "Boolean?")
                        codeWriter.WriteLine($"<InputCheckbox class=\"form-check\" @bind-Value=\"@_model.{propertyName}\" />");
                    if (propertyType.StartsWith("Int"))
                        codeWriter.WriteLine($"<InputNumber class=\"form-control\" @bind-Value=\"@_model.{propertyName}\" />");
                    codeWriter.WriteLine("</div>");
                    codeWriter.WriteLine("</div>");
                }
            }
            codeWriter.WriteLine("</EditForm>");
            codeWriter.WriteLine("@code{");
            codeWriter.WriteLine($"public {entityType.Name}Model _model {{get;set;}}=new {entityType.Name}Model();");
            codeWriter.WriteLine("}");
            var blazorPageCode = codeWriter.GenerateCode();
            var document = RazorSourceDocument.Create(blazorPageCode, $"Admin/{entityType.Name}/List.razor");
            var parsedDoc = RazorSyntaxTree.Parse(document);
            return blazorPageCode;
        }
    }

    public class TypeMapping
    {
        public Type SourceType { get; set; }
        public string SourceTypeName => SourceType.Name;
        public string DestModelName => $"{SourceTypeName}Model";
        public string DestModelTypeCodeString { get; set; }
        public BlazorCrud BlazorCrud { get; set; }
    }

    public class BlazorCrud
    {
        public string CreatePageCodeString { get; set; }
    }
}
