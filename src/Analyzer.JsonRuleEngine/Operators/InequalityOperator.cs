﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Templates.Analyzer.RuleEngines.JsonEngine.Operators
{
    /// <summary>
    /// An operator that evaluates the "greater", "greaterOrEquals", "less", and "lessOrEquals" JSON expressions.
    /// </summary>
    internal class InequalityOperator : LeafExpressionOperator
    {
        /// <inheritdoc/>
        public override string Name => GetName();

        /// <summary>
        /// Whether the operator also considers equality
        /// </summary>
        public Boolean OrEquals;

        /// <summary>
        /// Creates an InequalityOperator.
        /// </summary>
        /// <param name="specifiedValue">The value specified in the JSON rule.</param>
        /// <param name="isNegative">Whether the operator compares by greater than or by less than.</param>
        /// <param name="orEquals">Whether the operator also considers equality.</param>
        public InequalityOperator(JToken specifiedValue, bool isNegative, bool orEquals)
        {
            if (specifiedValue == null)
            {
                throw new ArgumentNullException(nameof(specifiedValue));
            }
            else
            {
                ValidateComparisonTerm(specifiedValue);
            }

            this.SpecifiedValue = specifiedValue;
            this.IsNegative = isNegative;
            this.OrEquals = orEquals;
        }

        /// <summary>
        /// Evaluates <paramref name="tokenToEvaluate"/> to determine if it is greater, greaterOrEquals, less, or lessOrEquals than the specified value.
        /// </summary>
        /// <param name="tokenToEvaluate">The JToken to evaluate.</param>
        /// <returns>A value indicating whether or not the evaluation passed.</returns>
        public override bool EvaluateExpression(JToken tokenToEvaluate)
        {
            if (tokenToEvaluate == null)
            {
                // If the specified property in the JSON is not defined then we would assume it could potentially have an undesired value
                return false;
            }

            ValidateComparisonTerm(tokenToEvaluate);

            // TODO throw this exception earlier?
            if ((SpecifiedValue.Type == JTokenType.Date && tokenToEvaluate.Type != JTokenType.Date) ||
                (tokenToEvaluate.Type == JTokenType.Date && SpecifiedValue.Type != JTokenType.Date))
            {
                throw new InvalidOperationException($"Cannot compare {SpecifiedValue.Type} with {tokenToEvaluate.Type} using an InequalityOperator");
            }

            var normalizedSpecifiedValue = GetNormalizedValue(SpecifiedValue);
            var normalizedTokenToEvaluate = GetNormalizedValue(tokenToEvaluate);

            var result = normalizedSpecifiedValue > normalizedTokenToEvaluate;

            if (IsNegative)
            {
                result = !result;
            }

            if (OrEquals)
            {
                result = result || normalizedSpecifiedValue == normalizedTokenToEvaluate;

            }

            return result;
        }

        // TODO throw this exception earlier?
        private void ValidateComparisonTerm(JToken term)
        {
            var validTypes = new JTokenType[] { JTokenType.Date, JTokenType.Float, JTokenType.Integer };

            if (!validTypes.Contains(term.Type))
            {
                throw new InvalidOperationException($"Cannot compare against a {term.Type} using an InequalityOperator");
            }
        }

        private double GetNormalizedValue(JToken token)
        {
            double value;

            if (token.Type == JTokenType.Date)
            {
                value = token.Value<DateTime>().ToOADate();
            }
            else
            {
                value = token.Value<double>();
            }

            return value;
        }

        private string GetName() {
            if (IsNegative && OrEquals)
            {
                return "LessOrEquals";
            }
            else if (IsNegative && !OrEquals)
            {
                return "Less";
            }
            else if (!IsNegative && OrEquals)
            {
                return "GreaterOrEquals";
            }
            else
            {
                return "Greater";
            }
        }
    }
}