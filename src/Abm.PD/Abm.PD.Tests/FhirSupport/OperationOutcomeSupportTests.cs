using Abm.PD.Domain.FhirSupport;
using Hl7.Fhir.Model;

namespace Abm.PD.Tests.FhirSupport;

public class OperationOutcomeSupportTests
{
    [Fact]
    public void ExtractErrorMessages_AnOutcomeWithNoIssuesYieldsNoMessages()
    {
        Assert.Empty(OperationOutcomeSupport.ExtractErrorMessages(new OperationOutcome()));
    }

    [Fact]
    public void ExtractErrorMessages_ComposesSeverityCodeDetailsAndDiagnostics()
    {
        OperationOutcome operationOutcome = new()
        {
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Processing,
                    Details = new CodeableConcept { Text = "The _typeFilter is not supported" },
                    Diagnostics = "Unsupported parameter"
                }
            ]
        };

        string message = Assert.Single(OperationOutcomeSupport.ExtractErrorMessages(operationOutcome));

        Assert.Equal(
            "Severity: error, Code: processing, Details: The _typeFilter is not supported, Diagnostics: Unsupported parameter",
            message);
    }

    [Fact]
    public void ExtractErrorMessages_OmitsThePartsTheIssueDoesNotCarry()
    {
        OperationOutcome operationOutcome = new()
        {
            Issue =
            [
                new OperationOutcome.IssueComponent { Severity = OperationOutcome.IssueSeverity.Warning }
            ]
        };

        string message = Assert.Single(OperationOutcomeSupport.ExtractErrorMessages(operationOutcome));

        Assert.Equal("Severity: warning", message);
    }

    [Fact]
    public void ExtractErrorMessages_AppendsEveryLocationAndExpression()
    {
        OperationOutcome operationOutcome = new()
        {
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Fatal,
                    Code = OperationOutcome.IssueType.Invalid,
                    Location = ["Patient.name[0]", "Patient.name[1]"],
                    Expression = ["Patient.name.given", "Patient.name.family"]
                }
            ]
        };

        string message = Assert.Single(OperationOutcomeSupport.ExtractErrorMessages(operationOutcome));

        Assert.Equal(
            "Severity: fatal, Code: invalid, Location: Patient.name[0], Location: Patient.name[1], " +
            "Expression: Patient.name.given, Expression: Patient.name.family",
            message);
    }

    [Fact]
    public void ExtractErrorMessages_ReturnsOneMessagePerIssueInOrder()
    {
        OperationOutcome operationOutcome = new()
        {
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Diagnostics = "first"
                },
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Information,
                    Diagnostics = "second"
                }
            ]
        };

        string[] messages = OperationOutcomeSupport.ExtractErrorMessages(operationOutcome);

        Assert.Equal(2, messages.Length);
        Assert.Contains("first", messages[0]);
        Assert.Contains("second", messages[1]);
    }

    [Fact]
    public void ExtractErrorMessages_IgnoresWhitespaceOnlyDetailsAndDiagnostics()
    {
        OperationOutcome operationOutcome = new()
        {
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Details = new CodeableConcept { Text = "   " },
                    Diagnostics = "  "
                }
            ]
        };

        Assert.Equal("Severity: error", Assert.Single(OperationOutcomeSupport.ExtractErrorMessages(operationOutcome)));
    }
}
