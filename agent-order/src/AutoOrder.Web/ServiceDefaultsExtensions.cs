namespace AutoOrder.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// サービス共通の Aspire 既定（サービスディスカバリ・回復性・ヘルスチェック・OpenTelemetry）を追加する。
// 利用は Web プロジェクトのみのため、独立した ServiceDefaults プロジェクトではなく Web に取り込んでいる。
internal static class ServiceDefaultsExtensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(static http =>
        {
            // 既定で回復性を有効化
            http.AddStandardResilienceHandler();

            // 既定でサービスディスカバリを有効化
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(static logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(static metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(static options =>
                        // ヘルスチェック要求はトレースから除外する
                        options.Filter = static context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // アプリが応答可能であることを確認する既定の liveness チェックを追加
            .AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // 非開発環境でヘルスチェックエンドポイントを公開するにはセキュリティ上の考慮が必要なため、開発環境のみ公開する。
        if (app.Environment.IsDevelopment())
        {
            // すべてのヘルスチェックが成功した場合に、起動後トラフィック受付可能とみなす
            app.MapHealthChecks(HealthEndpointPath);

            // "live" タグのチェックのみ成功すれば、アプリは生存しているとみなす
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = static r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
