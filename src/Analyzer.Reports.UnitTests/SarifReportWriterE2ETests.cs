﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using Azure.Deployments.Core.Extensions;
using FluentAssertions;
using Microsoft.Azure.Templates.Analyzer.Core;
using Microsoft.CodeAnalysis.Sarif;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;

namespace Microsoft.Azure.Templates.Analyzer.Reports.UnitTests
{
    [TestClass]
    public class SarifReportWriterE2ETests
    {
        [TestMethod]
        public void AnalyzeTemplateTests()
        {
            // arrange
            string targetDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Azure");
            var templateFilePath = new FileInfo(Path.Combine(targetDirectory, "SQLServerAuditingSettings.json"));

            var results = TemplateAnalyzer.Create(false).AnalyzeTemplate(
                template: ReadTemplate("SQLServerAuditingSettings.json"),
                parameters: null,
                templateFilePath: templateFilePath.FullName);

            // act
            var memStream = new MemoryStream();
            using (var writer = SetupWriter(memStream))
            {
                writer.WriteResults(results, (FileInfoBase)templateFilePath);
            }

            // assert
            var expectedLinesForRun = new Dictionary<string, List<List<int>>>
            {
                { "TA-000028", new List<List<int>> {
                        new List<int> { 23, 24, 25 },
                        new List<int> { 43, 44, 45 }
                    }
                },
                { "AZR-000186", new List<List<int>> {
                        new List<int> { 1 }
                    }
                },
                { "AZR-000187", new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }
                    }
                },
                { "AZR-000188", new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }

                    }
                },
                { "AZR-000189", new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }
                    }
                }
            };

            string artifactUriString = templateFilePath.Name;
            SarifLog sarifLog = JsonConvert.DeserializeObject<SarifLog>(Encoding.UTF8.GetString(memStream.ToArray()));
            sarifLog.Should().NotBeNull();

            Run run = sarifLog.Runs.First();
            run.Tool.Driver.Rules.Count.Should().Be(5);
            run.OriginalUriBaseIds.Count.Should().Be(1);
            run.OriginalUriBaseIds["ROOTPATH"].Uri.Should().Be(new Uri(targetDirectory, UriKind.Absolute));
            run.Results.Count.Should().Be(9);

            foreach (Result result in run.Results)
            {
                expectedLinesForRun.ContainsKey(result.RuleId).Should().BeTrue("Unexpected result found in SARIF");
                result.Level.Should().Be(FailureLevel.Error);

                var linesForResult = expectedLinesForRun[result.RuleId];
                var expectedLines = linesForResult.FirstOrDefault(l => l.Contains(result.Locations.First().PhysicalLocation.Region.StartLine));
                expectedLines.Should().NotBeNull("There shouldn't be a line number reported outside of the expected lines.");
                linesForResult.Remove(expectedLines);

                // Verify lines reported equal the expected lines
                result.Locations.Count.Should().Be(expectedLines.Count);
                foreach (var location in result.Locations)
                {
                    location.PhysicalLocation.ArtifactLocation.Uri.OriginalString.Should().BeEquivalentTo(artifactUriString);
                    var line = location.PhysicalLocation.Region.StartLine;

                    // Verify line is expected, and remove from the collection
                    expectedLines.Contains(line).Should().BeTrue();
                    expectedLines.Remove(line);
                }

                // Verify all lines were reported
                expectedLines.Should().BeEmpty();

                // Remove record for rule if all lines have been reported
                if (linesForResult.Count == 0)
                {
                    expectedLinesForRun.Remove(result.RuleId);
                }
            }

            // Verify all lines were reported
            expectedLinesForRun.Should().BeEmpty();
        }

        [DataTestMethod]
        [DataRow(true, DisplayName = "Templates in root directory")]
        [DataRow(true, "subPath", DisplayName = "Template in subdirectory")]
        [DataRow(false, "..", "anotherRepo", "subfolder", DisplayName = "Templates under root directory and outside of root")]
        public void AnalyzeDirectoryTests(bool secondTemplateUsesRelativePath, params string[] secondTemplatePathPieces)
        {
            // arrange
            var targetDirectory = Path.Combine(Directory.GetCurrentDirectory(), "repo");
            var secondTemplateName = "SQLServerAuditingSettings.json";
            var secondTemplateDirectory = Path.Combine(secondTemplatePathPieces.Prepend(targetDirectory).ToArray());
            var secondTemplatePath = Path.Combine(secondTemplateDirectory, secondTemplateName);
            var secondTemplateFileInfo = new FileInfo(secondTemplatePath);
            var expectedSecondTemplateFilePathInSarif = secondTemplateUsesRelativePath
                ? UriHelper.MakeValidUri(Path.Combine(secondTemplatePathPieces.Append(secondTemplateName).ToArray()))
                : new Uri(secondTemplatePath).AbsoluteUri;

            // act
            var memStream = new MemoryStream();
            using (var writer = SetupWriter(memStream, targetDirectory))
            {
                var analyzer = TemplateAnalyzer.Create(false);
                var templateFilePath = new FileInfo(Path.Combine(targetDirectory, "RedisCache.json"));
                var results = analyzer.AnalyzeTemplate(
                    template: ReadTemplate("RedisCache.json"),
                    parameters: null,
                    templateFilePath: templateFilePath.FullName);
                writer.WriteResults(results, (FileInfoBase)templateFilePath);

                results = analyzer.AnalyzeTemplate(
                    template: ReadTemplate("SQLServerAuditingSettings.json"),
                    parameters: null,
                    templateFilePath: secondTemplateFileInfo.FullName);
                writer.WriteResults(results, (FileInfoBase)secondTemplateFileInfo);
            }

            // assert
            SarifLog sarifLog = JsonConvert.DeserializeObject<SarifLog>(Encoding.UTF8.GetString(memStream.ToArray()));
            sarifLog.Should().NotBeNull();

            Run run = sarifLog.Runs.First();
            run.Tool.Driver.Rules.Count.Should().Be(8);
            run.Tool.Driver.Rules.Any(r => r.Id.Equals("TA-000022")).Should().Be(true);
            run.Tool.Driver.Rules.Any(r => r.Id.Equals("TA-000028")).Should().Be(true);
            run.OriginalUriBaseIds.Count.Should().Be(1);
            run.OriginalUriBaseIds["ROOTPATH"].Uri.Should().Be(new Uri(targetDirectory, UriKind.Absolute));

            var expectedLinesForRun = new Dictionary<string, (string file, string uriBase, List<List<int>> lines)>
            {
                { "TA-000022", (
                    file: "RedisCache.json",
                    uriBase: SarifReportWriter.UriBaseIdString,
                    lines: new List<List<int>> {
                        new List<int> { 20 }

                    })
                },
                { "TA-000028", (
                    file: expectedSecondTemplateFilePathInSarif,
                    uriBase: secondTemplateUsesRelativePath ? SarifReportWriter.UriBaseIdString : null,
                    lines: new List<List<int>> {
                        new List<int> { 23, 24, 25 },
                        new List<int> { 43, 44, 45 }
                    })
                },
                { "AZR-000164", (
                    file: "RedisCache.json",
                    uriBase: SarifReportWriter.UriBaseIdString,
                    lines: new List<List<int>> {
                        new List<int> { 19 }
                    })
                },
                { "AZR-000165", (
                    file: "RedisCache.json",
                    uriBase: SarifReportWriter.UriBaseIdString,
                    lines: new List<List<int>> {
                        new List<int> { 19 }
                    })
                },
                { "AZR-000186", (
                    file: expectedSecondTemplateFilePathInSarif,
                    uriBase: secondTemplateUsesRelativePath ? SarifReportWriter.UriBaseIdString : null,
                    lines: new List<List<int>> {
                        new List<int> { 1 }
                    })
                },
                { "AZR-000187", (
                    file: expectedSecondTemplateFilePathInSarif,
                    uriBase: secondTemplateUsesRelativePath ? SarifReportWriter.UriBaseIdString : null,
                    lines: new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }
                    })
                },
                { "AZR-000188", (
                    file: expectedSecondTemplateFilePathInSarif,
                    uriBase: secondTemplateUsesRelativePath ? SarifReportWriter.UriBaseIdString : null,
                    lines: new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }
                    })
                },
                { "AZR-000189", (
                    file: expectedSecondTemplateFilePathInSarif,
                    uriBase: secondTemplateUsesRelativePath ? SarifReportWriter.UriBaseIdString : null,
                    lines: new List<List<int>> {
                        new List<int> { 14 },
                        new List<int> { 34 }
                    })
                }
            };

            run.Results.Count.Should().Be(12);
            foreach (Result result in run.Results)
            {
                expectedLinesForRun.ContainsKey(result.RuleId).Should().BeTrue("Unexpected result found in SARIF");

                result.Locations.Count.Should().BeGreaterThan(0);

                // Find correct set of lines expected for result
                (var fileName, var uriBase, var linesForResult) = expectedLinesForRun[result.RuleId];
                var expectedLines = linesForResult.FirstOrDefault(l => l.Contains(result.Locations.First().PhysicalLocation.Region.StartLine));
                expectedLines.Should().NotBeNull("There shouldn't be a line number reported outside of the expected lines.");
                linesForResult.Remove(expectedLines);

                // Verify lines reported equal the expected lines
                result.Locations.Count.Should().Be(expectedLines.Count);
                foreach (var location in result.Locations)
                {
                    location.PhysicalLocation.ArtifactLocation.Uri.OriginalString.Should().BeEquivalentTo(fileName);
                    location.PhysicalLocation.ArtifactLocation.UriBaseId.Should().BeEquivalentTo(uriBase);
                    var line = location.PhysicalLocation.Region.StartLine;

                    // Verify line is expected, and remove from the collection
                    expectedLines.Contains(line).Should().BeTrue();
                    expectedLines.Remove(line);
                }

                // Verify all lines were reported
                expectedLines.Should().BeEmpty();

                // Remove record for rule if all lines have been reported
                if (linesForResult.Count == 0)
                {
                    expectedLinesForRun.Remove(result.RuleId);
                }

                result.Level.Should().Be(FailureLevel.Error);
            }

            // Verify all lines and results were reported
            expectedLinesForRun.Should().BeEmpty();
        }

        private static string ReadTemplate(string templateFileName)
        {
            return File.ReadAllText(Path.Combine("TestTemplates", templateFileName));
        }

        private static SarifReportWriter SetupWriter(Stream stream, string targetDirectory = null)
        {
            var mockFileSystem = new Mock<IFileInfo>();
            mockFileSystem
                .Setup(x => x.Create())
                .Returns(() => stream);
            return new SarifReportWriter(mockFileSystem.Object, targetPath: targetDirectory);
        }
    }
}