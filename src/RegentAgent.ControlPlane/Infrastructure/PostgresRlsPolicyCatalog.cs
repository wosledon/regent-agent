using System.Text;

namespace RegentAgent.ControlPlane.Infrastructure;

internal static class PostgresRlsPolicyCatalog
{
    private const string QuotedTokenPattern = "'(?:[^']|'')*'|\"(?:[^\"]|\"\")*\"";

    public const string TenantIsolationPredicate =
        "\"TenantId\"=nullif(current_setting('app.tenant_id',true),'')::uuid";

    public const string MembershipUserReadPredicate =
        "\"UserId\"=nullif(current_setting('app.user_id',true),'')::uuid";

    public static string NormalizedExpression(string expressionSql) =>
        $"lower(regexp_replace(regexp_replace(regexp_replace(" +
        $"regexp_replace({expressionSql}, {SqlLiteral(QuotedTokenPattern)}, '?', 'g'), " +
        "'[[:space:]]+', '', 'g'), '::text', '', 'g'), '[()]', '', 'g'))";

    public static string CanonicalTenantPolicy(string policyAlias, string classAlias) =>
        $"{policyAlias}.polname = {classAlias}.relname || '_tenant_isolation' " +
        $"AND {policyAlias}.polpermissive AND {policyAlias}.polcmd = '*' AND 0 = ANY({policyAlias}.polroles) " +
        $"AND {NormalizedExpressionEquals($"pg_get_expr({policyAlias}.polqual, {policyAlias}.polrelid)", TenantIsolationPredicate)} " +
        $"AND {NormalizedExpressionEquals($"pg_get_expr({policyAlias}.polwithcheck, {policyAlias}.polrelid)", TenantIsolationPredicate)}";

    public static string MembershipUserReadPolicy(string policyAlias, string classAlias) =>
        $"{classAlias}.relname = 'Memberships' AND {policyAlias}.polname = 'Memberships_user_read' " +
        $"AND {policyAlias}.polpermissive AND {policyAlias}.polcmd = 'r' AND 0 = ANY({policyAlias}.polroles) " +
        $"AND {NormalizedExpressionEquals($"pg_get_expr({policyAlias}.polqual, {policyAlias}.polrelid)", MembershipUserReadPredicate)}";

    private static string NormalizedExpressionEquals(string expressionSql, string expectedExpression)
    {
        var expected = TokenizeExpression(expectedExpression);
        return $"({NormalizedExpression(expressionSql)} = {SqlLiteral(expected.Structure)} " +
               $"AND {QuotedTokensSql(expressionSql)} = {SqlTextArray(expected.QuotedTokens)})";
    }

    private static string QuotedTokensSql(string expressionSql) =>
        $"coalesce((SELECT array_agg(token[1] ORDER BY ordinal) " +
        $"FROM regexp_matches({expressionSql}, {SqlLiteral(QuotedTokenPattern)}, 'g') " +
        "WITH ORDINALITY AS matched(token, ordinal)), ARRAY[]::text[])";

    internal static (string Structure, IReadOnlyList<string> QuotedTokens) TokenizeExpression(string expression)
    {
        var structure = new StringBuilder();
        var quotedTokens = new List<string>();
        var index = 0;
        while (index < expression.Length)
        {
            var character = expression[index];
            if (character is '\'' or '"')
            {
                var quote = character;
                var start = index;
                index++;
                var closed = false;
                while (index < expression.Length)
                {
                    if (expression[index] != quote)
                    {
                        index++;
                        continue;
                    }
                    if (index + 1 < expression.Length && expression[index + 1] == quote)
                    {
                        index += 2;
                        continue;
                    }
                    index++;
                    closed = true;
                    break;
                }
                if (!closed) throw new ArgumentException("SQL expression contains an unterminated quoted token.", nameof(expression));
                quotedTokens.Add(expression[start..index]);
                structure.Append('?');
                continue;
            }
            if (char.IsWhiteSpace(character) || character is '(' or ')')
            {
                index++;
                continue;
            }
            if (index + 6 <= expression.Length &&
                expression.AsSpan(index, 6).Equals("::text", StringComparison.OrdinalIgnoreCase))
            {
                index += 6;
                continue;
            }
            structure.Append(char.ToLowerInvariant(character));
            index++;
        }
        return (structure.ToString(), quotedTokens);
    }

    private static string SqlTextArray(IEnumerable<string> values)
    {
        var items = values.Select(SqlLiteral).ToArray();
        return items.Length == 0 ? "ARRAY[]::text[]" : $"ARRAY[{string.Join(", ", items)}]::text[]";
    }

    public static string SqlLiteral(string value) => "'" + value.Replace("'", "''") + "'";
}
