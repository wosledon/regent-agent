using RegentAgent.ControlPlane.Infrastructure;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PostgresRlsPolicyCatalogTests
{
    [Fact]
    public void Tokenizer_preserves_literal_case_spaces_and_quoted_identifier_case()
    {
        var canonical = PostgresRlsPolicyCatalog.TokenizeExpression(PostgresRlsPolicyCatalog.TenantIsolationPredicate);
        var settingCase = PostgresRlsPolicyCatalog.TokenizeExpression(
            "\"TenantId\" = nullif(current_setting('APP.TENANT_ID', true), ''::text)::uuid");
        var settingSpace = PostgresRlsPolicyCatalog.TokenizeExpression(
            "\"TenantId\" = nullif(current_setting('app. tenant_id', true), ''::text)::uuid");
        var columnCase = PostgresRlsPolicyCatalog.TokenizeExpression(
            "\"tenantid\" = nullif(current_setting('app.tenant_id', true), ''::text)::uuid");

        Assert.Equal(canonical.Structure, settingCase.Structure);
        Assert.Equal(canonical.Structure, settingSpace.Structure);
        Assert.Equal(canonical.Structure, columnCase.Structure);
        Assert.NotEqual(canonical.QuotedTokens, settingCase.QuotedTokens);
        Assert.NotEqual(canonical.QuotedTokens, settingSpace.QuotedTokens);
        Assert.NotEqual(canonical.QuotedTokens, columnCase.QuotedTokens);
    }

    [Fact]
    public void Tokenizer_ignores_only_unquoted_formatting_differences()
    {
        var canonical = PostgresRlsPolicyCatalog.TokenizeExpression(PostgresRlsPolicyCatalog.TenantIsolationPredicate);
        var formatted = PostgresRlsPolicyCatalog.TokenizeExpression(
            " ( \"TenantId\" ) = NULLIF ( CURRENT_SETTING ( 'app.tenant_id' , TRUE ) , ''::TEXT ) :: UUID ");

        Assert.Equal(canonical.Structure, formatted.Structure);
        Assert.Equal(canonical.QuotedTokens, formatted.QuotedTokens);
    }
}
