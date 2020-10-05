﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace System.Windows.Forms.SourceGenerators
{
    [Generator]
    public class EnumValidationGenerator : ISourceGenerator
    {
        private const string EnumValidatorStub = @"
// <auto-generated />
namespace EnumValidation
{
    internal static class EnumValidator
    {
        public static void Validate(System.Enum enumToValidate)
        {
            // This will be filled in by the generator once you call EnumValidator.Validate()
        }
    }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            SyntaxReceiver? syntaxReceiver = context.SyntaxReceiver as SyntaxReceiver;
            if (syntaxReceiver is null)
            {
                throw new InvalidOperationException("We were given the wrong syntax receiver.");
            }

            var enumsToValidate = GetEnumValidationInfo(context.Compilation, syntaxReceiver.ArgumentsToValidate);

            if (enumsToValidate.Any())
            {
                var sb = new StringBuilder();
                GenerateValidator(context, sb, enumsToValidate);

                context.AddSource("Validation.cs", sb.ToString());
            }
            else
            {
                context.AddSource("Validator.cs", EnumValidatorStub);
            }
        }

        private static void GenerateValidator(GeneratorExecutionContext context, StringBuilder sb, IEnumerable<EnumValidationInfo> infos)
        {
            sb.AppendLine(
@"// <auto-generated />
namespace EnumValidation
{
    internal static class EnumValidator
    {");

            foreach (var info in infos)
            {
                string indent = "        ";

                sb.AppendLine($"{indent}public static void Validate({info.EnumType} enumToValidate)");
                sb.AppendLine($"{indent}{{");

                GenerateValidateMethodBody(context, sb, info, indent + "    ");

                sb.AppendLine($"{indent}}}");
                sb.AppendLine();
            }

            sb.AppendLine(
@"    }
}");
        }

        private static void GenerateValidateMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            sb.AppendLine($"{indent}int intValue = (int)enumToValidate;");
            if (info.IsFlags)
            {
                GenerateFlagsValidationMethodBody(context, sb, info, indent);
            }
            else
            {
                GenerateSequenceValidationMethodBody(context, sb, info, indent);
            }
            sb.AppendLine($"{indent}throw new System.ComponentModel.InvalidEnumArgumentException(\"{info.ArgumentName}\", intValue, typeof({info.EnumType}));");
        }

        private static void GenerateFlagsValidationMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            int total = 0;
            foreach (var element in info.Elements)
            {
                total |= element.Value;
            }
            sb.AppendLine($"{indent}if ((intValue & {total}) == intValue) return;");
        }

        private static void GenerateSequenceValidationMethodBody(GeneratorExecutionContext context, StringBuilder sb, EnumValidationInfo info, string indent)
        {
            foreach ((int min, int max) in GetElementSets(context, info.Elements))
            {
                if (min == max)
                {
                    sb.AppendLine($"{indent}if (intValue == {min}) return;");
                }
                else
                {
                    sb.AppendLine($"{indent}if (intValue >= {min} && intValue <= {max}) return;");
                }
            }
        }

        private static IEnumerable<(int min, int max)> GetElementSets(GeneratorExecutionContext context, List<EnumElementInfo> elements)
        {
            int min = 0;
            int? max = null;
            foreach (var info in elements)
            {
                if (max == null || info.Value != max + 1)
                {
                    if (max != null)
                    {
                        yield return (min, max.Value);
                    }
                    min = info.Value;
                    max = info.Value;
                }
                else
                {
                    max = info.Value;
                }
            }
            if (max == null)
            {
                context.ReportDiagnostic(Diagnostic.Create("EV1", nameof(EnumValidationGenerator), $"Can't validate an enum that has no elements", DiagnosticSeverity.Error, DiagnosticSeverity.Error, true, 4));
                yield break;
            }
            yield return (min, max.Value);
        }

        private static IEnumerable<EnumValidationInfo> GetEnumValidationInfo(Compilation compilation, List<SyntaxNode> argumentsToValidate)
        {
            var flagsAttributeType = compilation.GetTypeByMetadataName("System.FlagsAttribute");

            var foundTypes = new HashSet<ITypeSymbol>();

            foreach (SyntaxNode argument in argumentsToValidate)
            {
                var semanticModel = compilation.GetSemanticModel(argument.SyntaxTree);

                var enumType = semanticModel.GetTypeInfo(argument).Type;
                if (enumType == null || foundTypes.Contains(enumType))
                {
                    continue;
                }

                foundTypes.Add(enumType);

                var isFlags = enumType.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, flagsAttributeType));

                var info = new EnumValidationInfo(enumType, argument.ToString(), isFlags);

                yield return info;
            }
        }
    }
}
