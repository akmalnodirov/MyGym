using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MyGym.Api.OpenApi;

/// <summary>
/// Narrows numeric schemas that .NET emits as <c>["integer", "string"]</c> or
/// <c>["number", "string"]</c> down to the numeric type alone.
/// <para>
/// .NET 10 describes every <c>int</c> and <c>decimal</c> as accepting either a JSON number or a
/// numeric string, because the deserialiser is lenient about quoted numbers on input. That is true
/// of input and irrelevant to output — System.Text.Json always *writes* these as JSON numbers — but
/// the document does not distinguish the two, so the union leaks into every generated client type.
/// In the Mini App that meant <c>weight: number | string</c> and <c>reps: number | string</c> on 64
/// properties, which would push a coercion into every piece of arithmetic in the app.
/// </para>
/// <para>
/// Only the <c>string</c> member is dropped. <c>null</c> is preserved, so a nullable decimal stays
/// <c>number | null</c> and the client keeps being forced to handle the null.
/// </para>
/// </summary>
internal sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
            return Task.CompletedTask;

        var isNumeric = type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number);

        if (isNumeric && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;

            // The pattern only ever existed to validate the quoted-number form that was just
            // removed; leaving it on a numeric type makes the schema self-contradictory, since
            // "pattern" applies to strings and would be vacuously true here.
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
