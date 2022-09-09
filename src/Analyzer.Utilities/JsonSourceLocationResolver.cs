﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Azure.Templates.Analyzer.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Templates.Analyzer.Utilities
{
    /// <summary>
    /// An <see cref="ISourceLocationResolver"/> used for resolving line numbers from an expanded JSON template to the original JSON template.
    /// </summary>
    public class JsonSourceLocationResolver : ISourceLocationResolver
    {
        private static readonly Regex resourceIndexInPath = new Regex(@"resources\[(?<index>\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly TemplateContext templateContext;

        /// <summary>
        /// Create a new instance with the given <see cref="TemplateContext"/>.
        /// </summary>
        /// <param name="templateContext">The template context to map JSON paths against.</param>
        public JsonSourceLocationResolver(TemplateContext templateContext)
        {
            this.templateContext = templateContext ?? throw new ArgumentNullException(nameof(templateContext));
        }

        /// <summary>
        /// Given a JSON path in an expanded JSON template, find the equivalent line number
        /// in the original JSON template.
        /// </summary>
        /// <param name="pathInExpandedTemplate">The path in the expanded template
        /// to find the line number of in the original template.</param>
        /// <returns>The line number of the equivalent location in the original template,
        /// or 1 if it can't be determined.</returns>
        public SourceLocation ResolveSourceLocation(string pathInExpandedTemplate)
        {
            JToken expandedTemplateRoot = this.templateContext.ExpandedTemplate;
            JToken originalTemplateRoot = this.templateContext.OriginalTemplate;

            if (pathInExpandedTemplate == null || originalTemplateRoot == null)
            {
                throw new ArgumentNullException(pathInExpandedTemplate == null
                    ? nameof(pathInExpandedTemplate)
                    : nameof(originalTemplateRoot));
            }

            // Attempt to find an equivalent JToken in the original template from the expanded template's path directly
            var tokenFromOriginalTemplate = originalTemplateRoot.InsensitiveToken(pathInExpandedTemplate, InsensitivePathNotFoundBehavior.LastValid);

            // If the JToken returned from looking up the expanded template path is
            // just pointing to the root of the original template, then
            // even the first property could not be found in the original template.
            if (tokenFromOriginalTemplate.Equals(originalTemplateRoot))
            {
                return new SourceLocation(this.templateContext.TemplateIdentifier, 1);
            }

            // If the path is in the resources array of the template
            var matches = resourceIndexInPath.Matches(pathInExpandedTemplate);
            if (matches.Count > 0)
            {
                // Get the path of the child resource in the expanded template
                string resourceWithIndex = string.Join('.', matches);

                // Verify the expanded template is available.
                // (Avoid throwing earlier since this is not always needed.)
                if (expandedTemplateRoot == null)
                {
                    throw new ArgumentNullException(nameof(expandedTemplateRoot));
                }

                string remainingPathAtResourceScope = pathInExpandedTemplate[(resourceWithIndex.Length + 1)..];

                if (!templateContext.ResourceMappings.TryGetValue(resourceWithIndex, out string originalResourcePath))
                {
                    return new SourceLocation(this.templateContext.TemplateIdentifier, 1);
                }

                if (!string.Equals(resourceWithIndex, originalResourcePath))
                {
                    tokenFromOriginalTemplate = originalTemplateRoot.InsensitiveToken($"{originalResourcePath}.{remainingPathAtResourceScope}", InsensitivePathNotFoundBehavior.LastValid);
                }
            }

            // Adds template's line number to an offset dependent on the parent (if applicable) template's position
            return new SourceLocation(
                this.templateContext.TemplateIdentifier,
                (tokenFromOriginalTemplate as IJsonLineInfo)?.LineNumber + this.templateContext.Offset ?? 1);
        }
    }
}