using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TelegramAIBot.AI.Tools;

internal static class MathTool
{
    public static ToolDefinition Create()
    {
        return new ToolDefinition(
            "math",
            "Calculate math expression. Supports +, -, *, /, ^, %, sqrt(), sin(), cos(), tan(), log(), ln(), pi, e, and parentheses.",
            new
            {
                type = "object",
                properties = new
                {
                    expression = new
                    {
                        type = "string",
                        description = "The mathematical expression to evaluate (e.g., '2 + 2', 'sqrt(16)', 'sin(pi/2)')"
                    }
                },
                required = new[] { "expression" }
            },
            async (string jsonParams) =>
            {
                // Parse the JSON string
                var parameters = JObject.Parse(jsonParams);
                var expression = parameters["expression"]?.ToString();
                
                if (string.IsNullOrWhiteSpace(expression))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "No expression provided"
                    }, Formatting.Indented);
                }

                try
                {
                    var result = EvaluateExpression(expression);
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        expression = expression,
                        result = result,
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        unix_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }, Formatting.Indented);
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        expression = expression,
                        error = ex.Message,
                        timestamp = DateTimeOffset.UtcNow.ToString("O")
                    }, Formatting.Indented);
                }
            }
        );
    }

    private static double EvaluateExpression(string expression)
    {
        // Remove whitespace
        expression = Regex.Replace(expression, @"\s+", "");
        
        // Replace constants
        expression = expression.Replace("pi", Math.PI.ToString(CultureInfo.InvariantCulture));
        expression = expression.Replace("e", Math.E.ToString(CultureInfo.InvariantCulture));
        
        // Handle functions
        expression = HandleFunctions(expression);
        
        // Evaluate using DataTable for basic arithmetic
        return EvaluateBasicExpression(expression);
    }

    private static string HandleFunctions(string expression)
    {
        var functionPatterns = new Dictionary<string, Func<double, double>>
        {
            { "sqrt", Math.Sqrt },
            { "sin", x => Math.Sin(x * Math.PI / 180.0) }, // degrees
            { "cos", x => Math.Cos(x * Math.PI / 180.0) }, // degrees
            { "tan", x => Math.Tan(x * Math.PI / 180.0) }, // degrees
            { "log10", Math.Log10 },
            { "ln", Math.Log },
            { "abs", Math.Abs },
            { "floor", Math.Floor },
            { "ceil", Math.Ceiling }
        };

        foreach (var func in functionPatterns)
        {
            var matches = Regex.Matches(expression, $@"{func.Key}\(([^()]+)\)");
            foreach (Match match in matches)
            {
                var innerExpr = match.Groups[1].Value;
                var innerValue = EvaluateExpression(innerExpr);
                var result = func.Value(innerValue);
                expression = expression.Replace(match.Value, result.ToString(CultureInfo.InvariantCulture));
            }
        }

        return expression;
    }

    private static double EvaluateBasicExpression(string expression)
    {
        // Handle exponentiation
        expression = HandleExponentiation(expression);
        
        // Use System.Data.DataTable for safe evaluation of basic arithmetic
        var table = new System.Data.DataTable();
        table.Columns.Add("result", typeof(double), expression);
        var row = table.NewRow();
        table.Rows.Add(row);
        
        return Convert.ToDouble(row["result"]);
    }

    private static string HandleExponentiation(string expression)
    {
        var powerPattern = new Regex(@"([\d\.]+)\^([\d\.]+)");
        while (powerPattern.IsMatch(expression))
        {
            expression = powerPattern.Replace(expression, match =>
            {
                var baseVal = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var expVal = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var result = Math.Pow(baseVal, expVal);
                return result.ToString(CultureInfo.InvariantCulture);
            });
        }
        return expression;
    }
}