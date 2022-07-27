﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.Templates.Analyzer.BicepProcessor;
using Microsoft.Azure.Templates.Analyzer.RuleEngines.JsonEngine;
using Microsoft.Azure.Templates.Analyzer.RuleEngines.PowerShellEngine;
using Microsoft.Azure.Templates.Analyzer.TemplateProcessor;
using Microsoft.Azure.Templates.Analyzer.Types;
using Microsoft.Azure.Templates.Analyzer.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Templates.Analyzer.Core
{
    /// <summary>
    /// This class runs the TemplateAnalyzer logic given the template and parameters passed to it.
    /// </summary>
    public class TemplateAnalyzer
    {
        /// <summary>
        /// Exception message when error during bicep template compilation
        /// </summary>
        public static readonly string BicepCompileErrorMessage = "Error compiling bicep template";

        private JsonRuleEngine jsonRuleEngine;
        private PowerShellRuleEngine powerShellRuleEngine;

        private ILogger logger;

        /// <summary>
        /// Private constructor to enforce use of <see cref="TemplateAnalyzer.Create"/> for creating new instances.
        /// </summary>
        /// <param name="jsonRuleEngine">The <see cref="JsonRuleEngine"/> to use in analyzing templates.</param>
        /// <param name="powerShellRuleEngine">The <see cref="PowerShellRuleEngine"/> to use in analyzing templates.</param>
        /// <param name="logger">A logger to report errors and debug information</param>
        private TemplateAnalyzer(JsonRuleEngine jsonRuleEngine, PowerShellRuleEngine powerShellRuleEngine, ILogger logger)
        {
            this.jsonRuleEngine = jsonRuleEngine;
            this.powerShellRuleEngine = powerShellRuleEngine;
            this.logger = logger;
        }

        /// <summary>
        /// Creates a new <see cref="TemplateAnalyzer"/> instance with the default built-in rules.
        /// </summary>
        /// <param name="usePowerShell">Whether or not to use PowerShell rules to analyze the template.</param>
        /// <param name="logger">A logger to report errors and debug information</param>
        /// <returns>A new <see cref="TemplateAnalyzer"/> instance.</returns>
        public static TemplateAnalyzer Create(bool usePowerShell, ILogger logger = null)
        {
            string rules;
            try
            {
                rules = LoadRules();
            }
            catch (Exception e)
            {
                throw new TemplateAnalyzerException("Failed to read rules.", e);
            }

            return new TemplateAnalyzer(
                JsonRuleEngine.Create(
                    rules,
                    templateContext => templateContext.IsBicep
                        ? new BicepLocationResolver(templateContext)
                        : new JsonLineNumberResolver(templateContext),
                    logger),
                usePowerShell ? new PowerShellRuleEngine(logger) : null,
                logger);
        }

        /// <summary>
        /// Runs the TemplateAnalyzer logic given the template and parameters passed to it.
        /// </summary>
        /// <param name="template">The ARM Template JSON</param>
        /// <param name="parameters">The parameters for the ARM Template JSON</param>
        /// <param name="templateFilePath">The ARM Template file path. (Needed to run arm-ttk checks.)</param>
        /// <returns>An enumerable of TemplateAnalyzer evaluations.</returns>
        public IEnumerable<IEvaluation> AnalyzeTemplate(string template, string parameters = null, string templateFilePath = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            // if the template is bicep, convert to JSON and get source map
            var isBicep = templateFilePath != null && templateFilePath.ToLower().EndsWith(".bicep", StringComparison.OrdinalIgnoreCase);
            object sourceMap = null;
            if (isBicep)
            {
                try
                {
                    (template, sourceMap) = BicepTemplateProcessor.ConvertBicepToJson(templateFilePath);
                }
                catch (Exception e)
                {
                    throw new TemplateAnalyzerException(BicepCompileErrorMessage, e);
                }
            }
            return DeepAnalyzeTemplate(template, parameters, templateFilePath, template, 0, isBicep, sourceMap);
        }

        /// <summary>
        /// Runs TemplateAnalyzer logic accounting for nested templates
        /// </summary>
        /// <param name="initialTemplate">The ARM Template JSON</param>
        /// <param name="parameters">The parameters for the ARM Template JSON</param>
        /// <param name="templateFilePath">The ARM Template file path. (Needed to run arm-ttk checks.)</param>
        /// <param name="modifiedTemplate">The ARM Template JSON with inherited parameters, variables, and functions if applicable</param>
        /// <param name="offset">The offset number for line numbers</param>
        /// <param name="isBicep">Is Bicep or not</param>
        /// <param name="sourceMap">sourceMap</param>
        /// <returns>An enumerable of TemplateAnalyzer evaluations.</returns>
        private IEnumerable<IEvaluation> DeepAnalyzeTemplate(string initialTemplate, string parameters, string templateFilePath, string modifiedTemplate, int offset, bool isBicep, object sourceMap)
        {
            JToken templatejObject;
            var armTemplateProcessor = new ArmTemplateProcessor(modifiedTemplate, logger: this.logger);

            try
            {
                templatejObject = armTemplateProcessor.ProcessTemplate(parameters);
            }
            catch (Exception e)
            {
                throw new TemplateAnalyzerException("Error while processing template.", e);
            }

            var templateContext = new TemplateContext
            {
                OriginalTemplate = JObject.Parse(initialTemplate),
                ExpandedTemplate = templatejObject,
                IsMainTemplate = true,
                ResourceMappings = armTemplateProcessor.ResourceMappings,
                TemplateIdentifier = templateFilePath,
                IsBicep = isBicep,
                SourceMap = sourceMap,
                Offset = offset
            };

            try
            {
                IEnumerable<IEvaluation> evaluations = this.jsonRuleEngine.AnalyzeTemplate(templateContext);

                if (this.powerShellRuleEngine != null && templateContext.TemplateIdentifier != null)
                {
                    this.logger?.LogDebug("Running PowerShell rule engine");
                    evaluations = evaluations.Concat(this.powerShellRuleEngine.AnalyzeTemplate(templateContext));
                }

                // For each rule we don't want to report the same line more than once
                // This is a temporal fix
                var evalsToValidate = new List<IEvaluation>();
                var evalsToNotValidate = new List<IEvaluation>();
                foreach (var eval in evaluations)
                {
                    if (!eval.Passed && eval.Result != null)
                    {
                        evalsToValidate.Add(eval);
                    }
                    else
                    {
                        evalsToNotValidate.Add(eval);
                    }
                }
                var uniqueResults = new Dictionary<(string, int), IEvaluation>();
                foreach (var eval in evalsToValidate)
                {
                    uniqueResults.TryAdd((eval.RuleId, eval.Result.LineNumber), eval);
                }
                evaluations = uniqueResults.Values.Concat(evalsToNotValidate);

                // Code to handle nested templates recursively
                dynamic jsonTemplate = JsonConvert.DeserializeObject(initialTemplate);
                dynamic jsonResources = jsonTemplate.resources; 
                // It seems to me like JObject.Parse(initialTemplate) is the same as templateObject but with line numbers  TODO ;;;
                dynamic processedTemplateResources = templatejObject["resources"];
                dynamic processedTemplateResourcesWithLineNumbers = templateContext.OriginalTemplate["resources"]; //.

                for (int i = 0; i < jsonResources.Count; i++)
                {
                    dynamic currentResource = jsonResources[i];
                    dynamic currentProcessedResource = processedTemplateResources[i];
                    dynamic currentProcessedResourceWithLineNumbers = processedTemplateResourcesWithLineNumbers[i]; //.

                    if (currentResource.type == "Microsoft.Resources/deployments")
                    {
                        dynamic nestedTemplate = currentResource.properties.template;
                        dynamic nestedTemplateWithLineNumbers = currentProcessedResourceWithLineNumbers.properties.template; //.
                        dynamic modifiedNestedTemplate = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(nestedTemplate));
                        // get the offset
                        int nextOffset = (nestedTemplateWithLineNumbers as IJsonLineInfo).LineNumber + offset - 1; // off by one
                        // check whether scope is set to inner or outer
                        var scope = currentResource.properties.expressionEvaluationOptions?.scope;
                        if (scope == null)
                        {
                            scope = "outer";
                        }
                        if (scope == "inner")
                        {
                            // allow for passing of params, variables and functions but evaluate everything else in the inner context
                            // check for params, variables and functions in parent, extract them and append them to the params, variables and functions
                            // of the child, and overwrite those of the child if needed. 
                            JToken passedParameters = currentProcessedResource.properties.parameters;
                            JToken passedVariables = currentProcessedResource.properties.variables;
                            JToken passedFunctions = currentProcessedResource.properties.functions;

                            // merge 
                            modifiedNestedTemplate.variables?.Merge(passedVariables);
                            modifiedNestedTemplate.functions?.Merge(passedFunctions);

                            dynamic currentPassedParameter = passedParameters?.First;
                            while (currentPassedParameter != null)
                            {
                                var value = currentPassedParameter.Value.value;
                                if (value != null)
                                {
                                    currentPassedParameter.Value.defaultValue = value;
                                    currentPassedParameter.Value.Remove("value");
                                }
                                currentPassedParameter = currentPassedParameter.Next;
                            }

                            modifiedNestedTemplate.parameters?.Merge(passedParameters);

                            int startOfTemplate = (nestedTemplateWithLineNumbers as IJsonLineInfo).LineNumber;

                            string stringNestedTemplate = extractNestedTemplate(initialTemplate, startOfTemplate);
                            //string stringNestedTemplate = JsonConvert.SerializeObject(nestedTemplate, Formatting.Indented);
                            string stringModifiedNestedTemplate = JsonConvert.SerializeObject(modifiedNestedTemplate, Formatting.Indented);
                                                      
                            IEnumerable<IEvaluation> result = DeepAnalyzeTemplate(stringNestedTemplate, parameters, templateFilePath, stringModifiedNestedTemplate, nextOffset, isBicep, sourceMap);

                            evaluations = evaluations.Concat(result);
                        }
                        else
                        {
                            // inner nested variables and params do not matter and just use whatever the parent passes down
                            modifiedNestedTemplate.variables = jsonTemplate.variables;
                            modifiedNestedTemplate.parameters = jsonTemplate.parameters;
                            modifiedNestedTemplate.functions = jsonTemplate.functions;

                            int startOfTemplate = (nestedTemplateWithLineNumbers as IJsonLineInfo).LineNumber;

                            string stringNestedTemplate = extractNestedTemplate(initialTemplate, startOfTemplate);
                            string stringModifiedNestedTemplate = JsonConvert.SerializeObject(modifiedNestedTemplate, Formatting.Indented);
                            
                            IEnumerable<IEvaluation> result = DeepAnalyzeTemplate(stringNestedTemplate, parameters, templateFilePath, stringModifiedNestedTemplate, nextOffset, isBicep, sourceMap);

                            evaluations = evaluations.Concat(result);
                        }                     
                    }
                }

                return evaluations;              
            }
            catch (Exception e)
            {
                throw new TemplateAnalyzerException("Error while evaluating rules.", e);
            }
        }

        /// <summary>
        /// Extracts a nested template in the exact format as user input, accounting for all white space and formatting
        /// </summary>
        /// <param name="template">Parent template containing nested template</param>
        /// <param name="startOfTemplate">Line number where the nested template starts</param>
        /// <returns>A nested template string</returns>

        private string extractNestedTemplate(string template, int startOfTemplate)
        {
            bool startOfNestingFound = false;
            int lineNumberCounter = 1;
            int curlyBraceCounter = 0;
            string stringNestedTemplate = "";
            foreach (var myString in template.Split(Environment.NewLine))
            {
                if (lineNumberCounter < startOfTemplate)
                {
                    lineNumberCounter += 1;
                    continue;
                }
                if (!startOfNestingFound)
                {
                    if (myString.Contains('{'))
                    {
                        stringNestedTemplate += myString.Substring(myString.IndexOf('{'));
                        stringNestedTemplate += Environment.NewLine;
                        startOfNestingFound = true;
                        lineNumberCounter += 1;
                        curlyBraceCounter += 1;
                    }
                    continue;
                }
                // after finding the start of nesting, count the opening and closing braces till they match up
                int inlineCounter = 1;
                foreach (char c in myString)
                {
                    if (c == '{') curlyBraceCounter++;
                    if (c == '}') curlyBraceCounter--;
                    if (curlyBraceCounter == 0) // done
                    {
                        stringNestedTemplate += myString[..inlineCounter];
                        break;
                    }
                    inlineCounter++;
                }

                if (curlyBraceCounter == 0)
                {
                    break;
                }

                //not done
                stringNestedTemplate += myString + Environment.NewLine;
                lineNumberCounter += 1;
            }

            return stringNestedTemplate;
        }

        private static string LoadRules()
        {
            return File.ReadAllText(
                Path.Combine(
                    Path.GetDirectoryName(AppContext.BaseDirectory),
                    "Rules/BuiltInRules.json"));
        }

        /// <summary>
        /// Modifies the rules to run based on values defined in the configuration file.
        /// </summary>
        /// <param name="configuration">The configuration specifying rule modifications.</param>
        public void FilterRules(ConfigurationDefinition configuration)
        {
            jsonRuleEngine.FilterRules(configuration);
        }
    }
}
