using Humanizer;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        private SourceFilesGeneratorConfiguration _sourceFilesGeneratorConfiguration;

        public SourceFilesGenerator(SourceFilesGeneratorConfiguration sourceFilesGeneratorConfiguration)
        {
            _sourceFilesGeneratorConfiguration = sourceFilesGeneratorConfiguration;
        }

        public void GenerateFiles()
        {
            if (this._sourceFilesGeneratorConfiguration is null)
                throw new Exception($"{nameof(this._sourceFilesGeneratorConfiguration)} is empty");
            List<TypeMapping> mappedTypes = new List<TypeMapping>(); ;
            var assembliesInDir = Directory.GetFiles(this._sourceFilesGeneratorConfiguration.SourceAssembliesDirectory, "*.dll");
            foreach (var singleAssemblyFile in assembliesInDir)
            {
                try
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(singleAssemblyFile);
                }
                catch (Exception ex)
                {

                }
            }

            var dataAccessAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .Where(p => p.GetName().Name == this._sourceFilesGeneratorConfiguration.SourceDataAccessAssemblyName).Single();
            var dbContext = dataAccessAssembly.GetTypes().Where(p => p.BaseType.Name == "DbContext").Single();
            var dbSets = dbContext.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var entitTypes = dbSets.Select(p => p.PropertyType.GenericTypeArguments.Single()).ToList();
            foreach (var singleDbSet in dbSets)
            {
                var entityType = singleDbSet.PropertyType.GenericTypeArguments.Single();
                var entityProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                string csCode = GenerateModelCode(dataAccessAssembly, entityType, entityProperties, this._sourceFilesGeneratorConfiguration.KeepNullable);
                string blazorAddPageCode = GenerateBlazorAddPageCode(dataAccessAssembly, entityType, entityProperties, entitTypes, this._sourceFilesGeneratorConfiguration.BasePagesRoute);
                string blazorListPageCode = GenerateBlazorListPageCode(dataAccessAssembly, entityType, entityProperties, this._sourceFilesGeneratorConfiguration.BasePagesRoute);
                string apiControllerCode = GenerateApiControllerCode(dataAccessAssembly, entityType, entityProperties, dbContext.Name, dbContext.Namespace,
                    this._sourceFilesGeneratorConfiguration.SourceEntitiesNamespace);
                TypeMapping typeMapping = new TypeMapping()
                {
                    SourceType = entityType,
                    DestModelTypeCodeString = csCode,
                    BlazorCrud = new BlazorCrud()
                    {
                        CreatePageCodeString = blazorAddPageCode,
                        ListPageCodeString = blazorListPageCode
                    },
                    DestApiControllerCodeString = apiControllerCode
                };
                mappedTypes.Add(typeMapping);
            }
            foreach (var singleMappedType in mappedTypes)
            {
                string modelFileName = Path.Combine(this._sourceFilesGeneratorConfiguration.ModelsDestinationFolder, $"{singleMappedType.DestModelName}.cs");
                File.WriteAllText(modelFileName, singleMappedType.DestModelTypeCodeString);

                string blazorCreatePageFileName = Path.Combine(this._sourceFilesGeneratorConfiguration.BlazorFilesDestinationFolder, @$"{singleMappedType.SourceTypeName}", $"Add.razor");
                var blazorCreatePagedir = Directory.GetParent(blazorCreatePageFileName);
                if (!Directory.Exists(blazorCreatePagedir.FullName))
                    Directory.CreateDirectory(blazorCreatePagedir.FullName);
                File.WriteAllText(blazorCreatePageFileName, singleMappedType.BlazorCrud.CreatePageCodeString);

                string blazorListPageFileName = Path.Combine(this._sourceFilesGeneratorConfiguration.BlazorFilesDestinationFolder, @$"{singleMappedType.SourceTypeName}", $"List.razor");
                File.WriteAllText(blazorListPageFileName, singleMappedType.BlazorCrud.ListPageCodeString);

                string apiControllerFileName = Path.Combine(this._sourceFilesGeneratorConfiguration.ApiControllersDestinationFolder, $"{singleMappedType.SourceType.Name}Controller.cs");
                File.WriteAllText(apiControllerFileName, singleMappedType.DestApiControllerCodeString);
            }
            var globalProfileCode = GenerateAutoMappingGlobalProfile(entitTypes, this._sourceFilesGeneratorConfiguration.SourceEntitiesNamespace);
            string autoMapperProfileFileName = Path.Combine(this._sourceFilesGeneratorConfiguration.AutoMappingProfileDestinationFolder, "GlobalMappingProfile.cs");
            if (!Directory.Exists(Directory.GetParent(autoMapperProfileFileName).FullName))
            {
                Directory.CreateDirectory(Directory.GetParent(autoMapperProfileFileName).FullName);
            }
            File.WriteAllText(autoMapperProfileFileName, globalProfileCode);
        }

        private static string GenerateModelCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties,
            bool keepNullable)
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendLine("using System;");
            strBuilder.AppendLine("using System.ComponentModel.DataAnnotations;");
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
                    if (keepNullable)
                    {
                        propertyType =
                            $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.First().Name}?";
                    }
                    else
                    {
                        propertyType =
                            $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.First().Name}";
                    }
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
                    var customAttributes = singlePropertyinEntity.GetCustomAttributes();
                    if (customAttributes.Count() > 0)
                    {
                        StringLengthAttribute stringLengthAttribute = singlePropertyinEntity.GetCustomAttribute<StringLengthAttribute>();
                        if (stringLengthAttribute != null)
                        {
                            strBuilder.AppendLine($"[StringLength({stringLengthAttribute.MaximumLength})]");
                        }
                        RequiredAttribute requiredAttribute = singlePropertyinEntity.GetCustomAttribute<RequiredAttribute>();
                        if (requiredAttribute != null)
                        {
                            strBuilder.AppendLine("[Required]");
                        }
                    }
                    if (propertyType == "Guid" || propertyType == "Guid?")
                        propertyType = "String";
                    strBuilder.AppendLine($"public {propertyType} {propertyName} {{get;set;}}");
                }
            }
            strBuilder.AppendLine("}");
            var csCode = CSharpSyntaxTree.ParseText(strBuilder.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
            return csCode;
        }

        private static string GenerateBlazorAddPageCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties,
            List<Type> entitTypes, string basePageRoute = "/AutogeneratedPages")
        {
            CodeWriter codeWriter = new CodeWriter();
            codeWriter.WriteLine($"@page \"{basePageRoute}/{entityType.Name}/Add\"");
            codeWriter.WriteLine();
            codeWriter.WriteLine("<EditForm Model=\"@_model\" OnValidSubmit=\"OnValidSubmit\">");
            codeWriter.WriteLine("<DataAnnotationsValidator></DataAnnotationsValidator>");
            codeWriter.WriteLine("<ValidationSummary></ValidationSummary>");
            List<string> variables = new List<string>();
            foreach (var singlePropertyinEntity in entityProperties)
            {
                string propertyType = string.Empty;
                bool isNavigationProperty = false;
                if (singlePropertyinEntity.PropertyType.Name.Contains("ICollection"))
                {
                    propertyType = $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.Single().Name}Model[]";
                    isNavigationProperty = true;
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
                    if (isNavigationProperty)
                        continue;
                    variables.Add($"public {propertyType} _{propertyName} {{get;set;}}");
                    codeWriter.WriteLine("<div class=\"row\">");
                    codeWriter.WriteLine("<div class=\"col\">");
                    codeWriter.WriteLine($"<label class=\"form-label\">{propertyName}</label>");
                    if (propertyType == "String" || propertyType == "String?" || propertyType == "Guid" || propertyType == "Guid?")
                        codeWriter.WriteLine($"<InputText class=\"form-control\" @bind-Value=\"@_model.{propertyName}\" />");
                    else
                    if (propertyType == "Boolean" || propertyType == "Boolean?")
                        codeWriter.WriteLine($"<InputCheckbox class=\"form-check\" @bind-Value=\"@_model.{propertyName}\" />");
                    else
                    if (propertyType.StartsWith("Int") || propertyType == "Byte" || propertyType == "Byte?"
                        || propertyType == "Decimal" || propertyType == "Decimal?"
                        || propertyType == "Double" || propertyType == "Double?"
                        || propertyType == "Single" || propertyType == "Single?")
                        codeWriter.WriteLine($"<InputNumber class=\"form-control\" @bind-Value=\"@_model.{propertyName}\" />");
                    else
                    if (propertyType == "DateTime" || propertyType == "DateTime?"
                        || propertyType == "DateTimeOffset" || propertyType == "DateTimeOffset?")
                        codeWriter.WriteLine($"<InputDate class=\"form-control\" @bind-Value=\"@_model.{propertyName}\"></InputDate>");
                    else
                    {

                    }
                    codeWriter.WriteLine("</div>");
                    codeWriter.WriteLine("</div>");
                }
            }
            codeWriter.WriteLine("<button type=\"submit\" class=\"btn btn-primary\">Submit</button>");
            codeWriter.WriteLine("</EditForm>");
            codeWriter.WriteLine("@code{");
            codeWriter.WriteLine($"public {entityType.Name}Model _model {{get;set;}}=new {entityType.Name}Model();");
            codeWriter.WriteLine("[Inject]");
            codeWriter.WriteLine("private HttpClient _httpClient {get;set;}");
            codeWriter.WriteLine("[Inject]");
            codeWriter.WriteLine("private NavigationManager _navigationManager {get;set;}");
            codeWriter.WriteLine("private async Task OnValidSubmit()");
            codeWriter.WriteLine("{");
            codeWriter.WriteLine($"string requesturl = \"api/{entityType.Name}/Add{entityType.Name}\";");
            codeWriter.WriteLine($"var response = await this._httpClient.PostAsJsonAsync<{entityType.Name}Model>(requesturl, _model);");
            codeWriter.WriteLine("if (response.IsSuccessStatusCode)");
            codeWriter.WriteLine("{");
            codeWriter.WriteLine($"_navigationManager.NavigateTo(_navigationManager.Uri.Replace(\"{entityType.Name}/Add\", \"{entityType.Name}/List\"));");
            codeWriter.WriteLine("}");
            codeWriter.WriteLine("}");
            codeWriter.WriteLine("}");
            var blazorPageCode = codeWriter.GenerateCode();
            var document = RazorSourceDocument.Create(blazorPageCode, $"Admin/{entityType.Name}/Add.razor");
            var parsedDoc = RazorSyntaxTree.Parse(document);
            return blazorPageCode;
        }

        private static string GenerateBlazorListPageCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties,
            string basePageRoute = "/AutogeneratedPages")
        {
            CodeWriter codeWriter = new CodeWriter();
            codeWriter.WriteLine($"@page \"{basePageRoute}/{entityType.Name}/List\"");
            codeWriter.WriteLine($"<a class=\"btn btn-primary\" href=\"{basePageRoute}/{entityType.Name}/Add\">Add new {entityType.Name.Humanize(LetterCasing.Title)}</a>");
            codeWriter.WriteLine();
            codeWriter.WriteLine($"@if (this._all{entityType.Name.Pluralize()} != null)");
            codeWriter.WriteLine("{");
            List<string> columnNames = new List<string>();
            foreach (var singlePropertyinEntity in entityProperties)
            {
                string propertyType = string.Empty;
                bool isNavigationProperty = false;
                if (singlePropertyinEntity.PropertyType.Name.Contains("ICollection"))
                {
                    propertyType = $"{singlePropertyinEntity.PropertyType.GenericTypeArguments.Single().Name}Model[]";
                    isNavigationProperty = true;
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
                    if (isNavigationProperty)
                        continue;
                    columnNames.Add(propertyName);
                }
            }
            codeWriter.WriteLine("<div class=\"row\">");
            foreach (var singleColumnName in columnNames)
            {
                codeWriter.WriteLine("<div class=\"col\">");
                codeWriter.WriteLine($"<strong>{singleColumnName.Humanize()}</strong>");
                codeWriter.WriteLine("</div>");
            }
            codeWriter.WriteLine("</div>");
            codeWriter.WriteLine($"@foreach (var single{entityType.Name} in this._all{entityType.Name.Pluralize()})");
            codeWriter.WriteLine("{");
            codeWriter.WriteLine("<div class=\"row\">");
            foreach (var singleColumnName in columnNames)
            {
                codeWriter.WriteLine("<div class=\"col\">");
                codeWriter.WriteLine($"@single{entityType.Name}.{singleColumnName}");
                codeWriter.WriteLine("</div>");
            }
            codeWriter.WriteLine("</div>");
            codeWriter.WriteLine("}");
            codeWriter.WriteLine("}");
            codeWriter.WriteLine("@code{");
            codeWriter.WriteLine($"private {entityType.Name}Model[] _all{entityType.Name.Pluralize()} {{get;set;}}");
            codeWriter.WriteLine("[Inject]");
            codeWriter.WriteLine("private HttpClient _httpClient {get;set;}");
            codeWriter.WriteLine("[Inject]");
            codeWriter.WriteLine("private NavigationManager _navigationManager {get;set;}");
            codeWriter.WriteLine("protected override async Task OnInitializedAsync()");
            codeWriter.WriteLine("{");
            codeWriter.WriteLine($"string requesturl = \"api/{entityType.Name}/List{entityType.Name.Pluralize()}\";");
            codeWriter.WriteLine($"this._all{entityType.Name.Pluralize()} = await this._httpClient.GetFromJsonAsync<{entityType.Name}Model[]>(requesturl);");
            codeWriter.WriteLine("}");
            codeWriter.WriteLine("}");
            var blazorPageCode = codeWriter.GenerateCode();
            var document = RazorSourceDocument.Create(blazorPageCode, $"Admin/{entityType.Name}/List.razor");
            var parsedDoc = RazorSyntaxTree.Parse(document);
            return blazorPageCode;
        }

        private static string GenerateAutoMappingGlobalProfile(List<Type> entityTpes, string entitiesNamespace)
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendLine("using AutoMapper;");
            strBuilder.AppendLine($"using {entitiesNamespace};");
            strBuilder.AppendLine("public class GlobalMappingProfile: Profile");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine("public GlobalMappingProfile()");
            strBuilder.AppendLine("{");
            foreach (var singleEntityType in entityTpes)
            {
                strBuilder.AppendLine($"this.CreateMap<{singleEntityType.Name}, {singleEntityType.Name}Model>();");
                strBuilder.AppendLine($"this.CreateMap<{singleEntityType.Name}Model, {singleEntityType.Name}>();");
            }
            strBuilder.AppendLine("}");
            strBuilder.AppendLine("}");
            var csCode = CSharpSyntaxTree.ParseText(strBuilder.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
            return csCode;
        }

        private static string GenerateApiControllerCode(Assembly dataAccessAssembly, Type entityType, PropertyInfo[] entityProperties,
            string dbContextName, string dbContextNamespace, string entitiesNamespace)
        {
            StringBuilder strBuilder = new StringBuilder();
            strBuilder.AppendLine("using Microsoft.AspNetCore.Mvc;");
            strBuilder.AppendLine("using System.Threading.Tasks;");
            strBuilder.AppendLine("using Microsoft.EntityFrameworkCore;");
            strBuilder.AppendLine("using System.Linq;");
            strBuilder.AppendLine("using AutoMapper;");
            strBuilder.AppendLine($"using {dbContextNamespace};");
            strBuilder.AppendLine($"using {entitiesNamespace};");
            strBuilder.AppendLine();
            strBuilder.AppendLine("[Route(\"api/[controller]\")]");
            strBuilder.AppendLine("[ApiController]");
            strBuilder.AppendLine($"public partial class {entityType.Name}Controller : ControllerBase");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine($"private {dbContextName} _{dbContextName.ToLower()};");
            strBuilder.AppendLine($"private IMapper _mapper;");
            strBuilder.AppendLine($"public {entityType.Name}Controller({dbContextName} {dbContextName.ToLower()}, IMapper mapper)");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine($"this._{dbContextName.ToLower()} = {dbContextName.ToLower()};");
            strBuilder.AppendLine($"this._mapper = mapper;");
            strBuilder.AppendLine("}");
            GenerateListEndpoint(entityType, dbContextName, strBuilder);
            GenerateAddEndpoint(entityType, dbContextName, strBuilder);
            strBuilder.AppendLine("}");
            var csCode = CSharpSyntaxTree.ParseText(strBuilder.ToString()).GetRoot().NormalizeWhitespace().ToFullString();
            return csCode;
        }

        private static void GenerateListEndpoint(Type entityType, string dbContextName, StringBuilder strBuilder)
        {
            strBuilder.AppendLine("/// <summary>");
            strBuilder.AppendLine($"/// Retrieves the list of {entityType.Name.Humanize().Pluralize()}");
            strBuilder.AppendLine("/// </summary>");
            strBuilder.AppendLine($"/// <returns>An array of <see cref=\"{entityType.Name}\"/> </returns>");
            strBuilder.AppendLine("[HttpGet(\"[action]\")]");
            strBuilder.AppendLine($"public async Task<{entityType.Name}Model[]> List{entityType.Name.Pluralize()}()");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine($"return await this._{dbContextName.ToLower()}.{entityType.Name}.Select(p => this._mapper.Map<{entityType.Name}, {entityType.Name}Model>(p)).ToArrayAsync();");
            strBuilder.AppendLine("}");
        }

        private static void GenerateAddEndpoint(Type entityType, string dbContextName, StringBuilder strBuilder)
        {

            strBuilder.AppendLine("/// <summary>");
            strBuilder.AppendLine($"/// Adds a new record of {entityType.Name}");
            strBuilder.AppendLine("/// </summary>");
            strBuilder.AppendLine($"/// <param name=\"{entityType.Name.ToLower()}Model\"></param>");
            strBuilder.AppendLine($"/// <returns>The created {entityType.Name.ToLower()}Model. <see cref=\"{entityType.Name}Model\"/></returns>");
            strBuilder.AppendLine("[HttpPost(\"[action]\")]");
            strBuilder.AppendLine($"public async Task<{entityType.Name}Model> Add{entityType.Name}({entityType.Name}Model {entityType.Name.ToLower()}Model)");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine($"var entity = this._mapper.Map<{entityType.Name}Model,{entityType.Name}>({entityType.Name.ToLower()}Model);");
            strBuilder.AppendLine($"await _{dbContextName.ToLower()}.AddAsync(entity);");
            strBuilder.AppendLine($"await _{dbContextName.ToLower()}.SaveChangesAsync();");
            strBuilder.AppendLine($"var result = this._mapper.Map<{entityType.Name},{entityType.Name}Model>(entity);");
            strBuilder.AppendLine("return result;");
            strBuilder.AppendLine("}");
        }
    }

    public class TypeMapping
    {
        public Type SourceType { get; set; }
        public string SourceTypeName => SourceType.Name;
        public string DestModelName => $"{SourceTypeName}Model";
        public string DestModelTypeCodeString { get; set; }
        public BlazorCrud BlazorCrud { get; set; }
        public string DestApiControllerCodeString { get; set; }
    }

    public class BlazorCrud
    {
        public string CreatePageCodeString { get; set; }
        public string ListPageCodeString { get; set; }
    }

    public class SourceFilesGeneratorConfiguration
    {
        /// <summary>
        /// Folder where to place generated models files
        /// </summary>
        public string ModelsDestinationFolder { get; set; }
        /// <summary>
        /// Folder where to place generated blazor files
        /// </summary>
        public string BlazorFilesDestinationFolder { get; set; }
        /// <summary>
        /// Folder where to search for assemblies
        /// </summary>
        public string SourceAssembliesDirectory { get; set; }
        /// <summary>
        /// FullName for the assembly with the DbContext
        /// </summary>
        public string SourceDataAccessAssemblyName { get; set; }
        /// <summary>
        /// Folder where to place generated controller files
        /// </summary>
        public string ApiControllersDestinationFolder { get; set; }
        /// <summary>
        /// Namespace where to search for the Source Entity Framework entities
        /// </summary>
        public string SourceEntitiesNamespace { get; set; }
        /// <summary>
        /// Base route for the autogenenrated blazor pages
        /// </summary>
        public string BasePagesRoute { get; set; }
        /// <summary>
        /// When set to false, will force the generated properties to be non-nullable
        /// </summary>
        public bool KeepNullable { get; set; }
        /// <summary>
        /// Folder where to place generated automapper profile
        /// </summary>
        public string AutoMappingProfileDestinationFolder { get; set; }
    }
}
