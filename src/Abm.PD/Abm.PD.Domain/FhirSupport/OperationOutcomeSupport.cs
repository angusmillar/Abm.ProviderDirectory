using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;

namespace Abm.PD.Domain.FhirSupport;

public static class OperationOutcomeSupport
{
    public static string[] ExtractErrorMessages(OperationOutcome operationOutcome)
    {
        List<string> messages = new List<string>();

        if (operationOutcome.Issue is null)
        {
            return messages.ToArray();
        }
        foreach (var issue in operationOutcome.Issue)
        {
            StringBuilder sb = new StringBuilder($"Severity: {issue.Severity.GetLiteral()}");
            if (issue.Code.HasValue)
            {
                sb.Append($", Code: {issue.Code.GetLiteral()}");
            }
            
            if (!string.IsNullOrWhiteSpace(issue.Details?.Text))
            {
                sb.Append($", Details: {issue.Details.Text}");
            }
            
            if (!string.IsNullOrWhiteSpace(issue.Diagnostics))
            {
                sb.Append($", Diagnostics: {issue.Diagnostics}");
            }

            foreach (string issueLocation in issue.Location)
            {
                sb.Append($", Location: {issueLocation}");
            }
            
            foreach (string issueExpression in issue.Expression)
            {
                sb.Append($", Expression: {issueExpression}");
            }
            
            messages.Add(sb.ToString()); 
        }

        return messages.ToArray();
        
    }
}