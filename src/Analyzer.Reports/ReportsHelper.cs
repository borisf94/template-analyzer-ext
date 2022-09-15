﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Azure.Templates.Analyzer.Types;
using Microsoft.CodeAnalysis.Sarif;

namespace Microsoft.Azure.Templates.Analyzer.Reports
{
    /// <summary>
    /// 
    /// </summary>
    public class ReportsHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="evaluations"></param>
        /// <param name="filesToSkip"></param>
        /// <returns></returns>
        public static Dictionary<string, List<(IEvaluation, IList<IResult>)>> GetResultsByFile(
            IEnumerable<Types.IEvaluation> evaluations,
            IEnumerable<string> filesToSkip)
        {
            return GetResultsByFile(evaluations, filesToSkip, out int _);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="evaluations"></param>
        /// <param name="filesToSkip"></param>
        /// <param name="passedEvaluations"></param>
        /// <returns></returns>
        public static Dictionary<string, List<(IEvaluation, IList<IResult>)>> GetResultsByFile(
            IEnumerable<Types.IEvaluation> evaluations,
            IEnumerable<string> filesToSkip,
            out int passedEvaluations)
        {
            var resultsByFile = new Dictionary<string, List<(IEvaluation, IList<IResult>)>>();
            passedEvaluations = 0;


            foreach (var evaluation in evaluations)
            {
                if (!evaluation.Passed)
                {
                    (var actualFile, var failedResults) = GetResultsByFileInternal(evaluation);

                    // a file's results may have already been output if analyzing directory
                    if (filesToSkip.Contains(actualFile))
                    {
                        continue;
                    }

                    if (!resultsByFile.ContainsKey(actualFile))
                    {
                        resultsByFile[actualFile] = new List<(IEvaluation, IList<IResult>)>();
                    }

                    // skip any evaluations with duplicate results (i.e. two source locations from other templates refer to same result)
                    if (!resultsByFile[actualFile].Any(resultsTuple => evaluation.RuleId.Equals(resultsTuple.Item1.RuleId)
                        && Enumerable.SequenceEqual(resultsTuple.Item2, failedResults)))
                    {
                        resultsByFile[actualFile].Add((evaluation, failedResults));
                    }
                }
                else
                {
                    passedEvaluations++;
                }
            }

            foreach (var file in resultsByFile.Keys)
            {
                resultsByFile[file] = resultsByFile[file].Distinct().ToList();
            }

            return resultsByFile;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="evaluation"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static (string, IList<IResult>) GetResultsByFileInternal(Types.IEvaluation evaluation)
        {
            // get all distinct failed results in evaluation
            var failedResults = GetFailedResults(evaluation).Distinct().ToList();
            failedResults.Sort((x, y) => x.SourceLocation.LineNumber.CompareTo(y.SourceLocation.LineNumber));

            // assumption: all results in a top-level evaluation are in a single resource and therefore in a single source file, so we can just look at the first to get them all
            // TODO: validating assumption
            if (failedResults.Select(r => r.SourceLocation.FilePath).Distinct().Count() != 1) throw new Exception("not 1 actual source file in top level eval");

            var actualFile = failedResults.First().SourceLocation.FilePath;

            return (actualFile, failedResults);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="evaluation"></param>
        /// <param name="failedResults"></param>
        /// <returns></returns>
        public static List<IResult> GetFailedResults(Types.IEvaluation evaluation, List<IResult> failedResults = null)
        {
            failedResults ??= new List<IResult>();

            if (!evaluation.Result?.Passed ?? false)
            {
                failedResults.Add(evaluation.Result);
            }

            foreach (var eval in evaluation.Evaluations.Where(e => !e.Passed))
            {
                GetFailedResults(eval, failedResults);
            }

            return failedResults;
        }
    }
}
