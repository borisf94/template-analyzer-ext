﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Microsoft.Azure.Templates.Analyzer.Cli
{
    /// <summary>
    /// Holds a template file with a parameters file to use with analysis, if applicable.
    /// </summary>
    /// <param name="Template">Template to be analyzed.</param>
    /// <param name="Parameters">Parameters to use when analyzing the template.</param>
    public record TemplateAndParams(FileInfo Template, FileInfo Parameters);

    internal static class TemplateDiscovery
    {
        private static readonly IReadOnlyList<string> validSchemas = new List<string> {
            "https://schema.management.azure.com/schemas/2015-01-01/deploymentTemplate.json#",
            "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
            "https://schema.management.azure.com/schemas/2018-05-01/subscriptionDeploymentTemplate.json#",
            "https://schema.management.azure.com/schemas/2019-08-01/tenantDeploymentTemplate.json#",
            "https://schema.management.azure.com/schemas/2019-08-01/managementGroupDeploymentTemplate.json#"
        }.AsReadOnly();

        private static readonly IReadOnlyList<string> validTemplateProperties = new List<string> {
            "contentVersion",
            "apiProfile",
            "parameters",
            "variables",
            "functions",
            "resources",
            "outputs",
        }.AsReadOnly();

        /// <summary>
        /// Finds all templates within a directory, paired with any matching parameters file.
        /// </summary>
        /// <param name="directory">The directory to search for templates in.</param>
        /// <returns>An enumerable of templates matched with any relevant parameters files.</returns>
        public static IEnumerable<TemplateAndParams> DiscoverTemplatesAndParametersInDirectory(DirectoryInfo directory)
        {
            var armTemplates = directory.GetFiles(
                "*.json",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true
                })
                .Where(s => !s.Name.Contains(".parameters"))
                .Where(IsValidTemplate);

            var bicepTemplates = directory.GetFiles(
                "*.bicep",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true
                });

            return armTemplates
                .Concat(bicepTemplates)
                .SelectMany(FindParameterFilesForTemplate);
        }

        /// <summary>
        /// Checks if parameters*.json files are present according to naming standards here https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/parameter-files#file-name
        /// </summary>
        /// <param name="template">Template file to match parameters for</param>
        /// <returns>
        /// An enumerable of <see cref="TemplateAndParams"/> for each parameters file found that matches against the template.
        /// If none are found, the <see cref="TemplateAndParams.Parameters"/> file will be null in a single record returned.
        /// </returns>
        public static IEnumerable<TemplateAndParams> FindParameterFilesForTemplate(FileInfo template)
        {
            var parameterFiles = template.Directory.GetFiles(
                Path.GetFileNameWithoutExtension(template.Name) + ".parameters*.json",
                new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = false
                });

            return parameterFiles.Any()
                ? parameterFiles.Select(parameters => new TemplateAndParams(template, parameters))
                : new[] { new TemplateAndParams(template, null) };
        }

        /// <summary>
        /// Determines whether or not a file is valid template.
        /// If the file extension is ".bicep", it is assumed to be valid.
        /// Otherwise, the file must be a JSON file with the proper schema for ARM templates.
        /// </summary>
        /// <param name="file">The file to determine template validity on.</param>
        /// <returns>true if the file is a valid ARM template; false otherwise.</returns>
        public static bool IsValidTemplate(FileInfo file)
        {
            // assume bicep files are valid, they are compiled/verified later
            if (file.Extension.Equals(".bicep", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            using var fileStream = new StreamReader(file.OpenRead());
            var reader = new JsonTextReader(fileStream);

            reader.Read();
            if (reader.TokenType != JsonToken.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.Depth == 1 && reader.TokenType == JsonToken.PropertyName)
                {
                    if (string.Equals((string)reader.Value, "$schema", StringComparison.OrdinalIgnoreCase))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonToken.String)
                        {
                            return false;
                        }

                        return validSchemas.Any(schema => string.Equals((string)reader.Value, schema, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (!validTemplateProperties.Any(property => string.Equals((string)reader.Value, property, StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }
                }
            }

            return false;
        }
    }
}
