[![Build Status](https://github.com/Azure/template-analyzer/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Azure/template-analyzer/actions/workflows/dotnet.yml)

# Template Analyzer

## What is Template Analyzer?
Template Analyzer scans ARM ([Azure Resource Manager](https://docs.microsoft.com/azure/azure-resource-manager/templates/overview)) and [Bicep](https://docs.microsoft.com/azure/azure-resource-manager/bicep/) Infrastructure-as-Code (IaC) templates to ensure security and best practice checks are being followed before deployment of your Azure solutions.

Template Analyzer provides a simple and extensible solution to improve the security of your Azure resources before deployment and ensures your templates follow best practices. Template Analyzer is designed to be customizable - users can write their own checks and/or enforce only the checks that are relevant for them.

## Getting started with Template Analyzer
Download the latest Template Analyzer release in [the releases section](https://github.com/Azure/template-analyzer/releases).

To preview the rules that come bundled with Template Analyzer, explore [the built-in rules](docs/built-in-rules.md).

## Using Template Analyzer
Template Analyzer is executed via a command line.  There are two formats to invoke it:

`TemplateAnalyzer.exe analyze-template <template-path> [-p <parameters-path>] [-c <config-path>] [--report-format <format>] [-o <output-path>] [-v]`

or

`TemplateAnalyzer.exe analyze-directory <directory-path> [-c <config-path>] [--report-format <format>] [-o <output-path>] [-v]`

### Input
Template Analyzer accepts the following inputs:

Argument | Description
--- | ---
`<template-path>` | The path of the template to analyze
`<directory-path>` | The directory in which to search for templates (recursively finds and analyzes all ARM and Bicep templates in the directory and its subdirectories).<br/>ARM templates are identified by a '.json' file extension and a [valid top-level *$schema* property](https://docs.microsoft.com/azure/azure-resource-manager/templates/syntax#template-format)>. Bicep templates are identified by a '.bicep' file extension.
**(Optional)** `-p` or `--parameters-file-path` | A [parameters file](https://docs.microsoft.com/azure/azure-resource-manager/templates/parameter-files)
**(Optional)** `-c` or `--config-file-path` | A [configuration file](docs/customizing-evaluation-outputs.md) which sets custom settings for the analyzer.<br/>**If argument is not provided, Template Analyzer will attempt to load a configuration from *<_ExecutablePath_>/configuration.json* if the file exists.**.
**(Optional)** `--report-format` | Valid formats:<br/>*Console*: output results to the console in plain text. **(default)**<br/>*Sarif*: output results to a file in [SARIF](https://sarifweb.azurewebsites.net) format.
`-o` or `--output-file-path` | **(Required if `--report-format` is *Sarif*)**  File path to output SARIF results to.
**(Optional)** `-v` or `--verbose` | Shows details about the analysis
**(Optional)** `--include-non-security-rules` | Run all the rules against the templates, including non-security rules
**(Optional)** `--custom-json-rules-path` | The [JSON rules file](docs/authoring-json-rules.md) to use against the templates.<br/>If not specified, will use the [default JSON rule set that is shipped with the tool](docs/built-in-rules.md#json-based-rules).

 Template Analyzer runs the [configured rules](#understanding-and-customizing-rules) against the provided template and its corresponding [template parameters](https://docs.microsoft.com/azure/azure-resource-manager/templates/parameter-files), if specified. If no template parameters are specified, then Template Analyzer will check if templates with the [general naming standards defined by Microsoft](https://learn.microsoft.com/azure/azure-resource-manager/templates/parameter-files#file-name) are present in the same folder, otherwise it generates the minimum number of placeholder parameters to properly evaluate [template functions](https://docs.microsoft.com/azure/azure-resource-manager/templates/template-functions) in the template.

**Note**: Providing Template Analyzer with template parameter values will result in more accurate results as it will more accurately represent your deployments. The values provided to parameters may affect the evaluation of Template Analyzer rules, altering its results. That said, **DO NOT** save sensitive data (passwords, connection strings, etc.) in parameter files in your repositories. Instead, [retrieve these values from your template from Azure Key Vault](https://docs.microsoft.com/azure/azure-resource-manager/templates/key-vault-parameter?tabs=azure-cli#reference-secrets-with-static-id).

### Output
Results can be output in plain text to the console, or output to a file in SARIF format. Template Analyzer will exit with an error code if any errors or violations are found during a scan.

#### Console
Template Analyzer outputs the results of violated rules, the corresponding line numbers where rules failed, and a recommendation to remediate each violation.

For a template which deploys an API App that does not require HTTPS, running Template Analyzer on the template would produce output which looks similar to the following:
```
>TemplateAnalyzer.exe analyze-template "C:\Templates\azuredeploy.json"

File: C:\Templates\azuredeploy.json

        TA-000004: API app should only be accessible over HTTPS
                Severity: Medium
                Recommendation: Use HTTPS to ensure server/service authentication and protect data in transit from network layer eavesdropping attacks
                More information: https://github.com/Azure/template-analyzer/blob/main/docs/built-in-rules.md#ta-000004-api-app-should-only-be-accessible-over-https
                Result: Failed
                Line: 67

        Rules passed: 16

Execution summary:
        The execution completed successfully
```

#### SARIF
Results are written to the file specified (with the `-o` or `--output-file-path` argument) in [SARIF](https://sarifweb.azurewebsites.net) format.

#### Exit codes
| Scenario      | Exit Code |
| ----------- | ----------- |
| Success: Operation was successful | 0 |
| Error: Problem with command | 1 |
| Error: Invalid file or directory path | 2 |
| Error: Missing file or directory path | 3 |
| Error: Problem loading configuration file | 4 |
| Error: Invalid ARM template specified | 10 |
| Error: Invalid Bicep template specified | 11 |
| Violation: Scan found rule violations in analyzed template(s) | 20 |
| Error: An error was encountered trying to analyze a template | 21 |
| Violation + Error: Scan encountered both violations in template(s) and errors trying to analyze template(s) | 22 |

### Understanding and customizing rules
The analysis rules used by Template Analyzer are written in JSON, located in *Rules/BuiltInRules.json* (starting from the directory *TemplateAnalyzer.exe* is in). This file can be added to and/or modified to change the rules that are run. See the [documentation for more information about how to author Template Analyzer JSON rules](./docs/authoring-json-rules.md).

## Contributing
This project welcomes contributions and suggestions. Please see the [Contribution Guide](./CONTRIBUTING.md) for more details about how to contribute to Template Analyzer. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repositories using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks
This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow [Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general). Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party's policies.

## Usage
This project follows [Microsoft Privacy Standards](https://go.microsoft.com/fwlink/?LinkId=521839). This product does not collect nor store any personal data.
