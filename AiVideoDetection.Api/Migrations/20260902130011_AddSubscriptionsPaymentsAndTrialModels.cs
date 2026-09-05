using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionsPaymentsAndTrialModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_identities",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    device_fingerprint_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    hash_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    risk_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_identities", x => x.id);
                    table.CheckConstraint("ck_device_identities_hash_present", "(device_token_hash IS NOT NULL AND device_token_hash <> '') OR (device_fingerprint_hash IS NOT NULL AND device_fingerprint_hash <> '')");
                });

            migrationBuilder.CreateTable(
                name: "free_trial_account_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    allocated_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    consumed_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_free_trial_account_usages", x => x.id);
                    table.CheckConstraint("ck_free_trial_account_usage_counts", "allocated_scans > 0 AND consumed_scans >= 0 AND reserved_scans >= 0 AND consumed_scans + reserved_scans <= allocated_scans");
                    table.ForeignKey(
                        name: "FK_free_trial_account_usages_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "free_trial_ip_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ip_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    hash_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    allocated_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    consumed_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_free_trial_ip_usages", x => x.id);
                    table.CheckConstraint("ck_free_trial_ip_usage_counts", "allocated_scans > 0 AND consumed_scans >= 0 AND reserved_scans >= 0 AND consumed_scans + reserved_scans <= allocated_scans");
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    scan_limit = table.Column<int>(type: "integer", nullable: false),
                    max_video_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    allows_smart_scan = table.Column<bool>(type: "boolean", nullable: false),
                    allows_detailed_scan = table.Column<bool>(type: "boolean", nullable: false),
                    validity_days = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.id);
                    table.CheckConstraint("ck_subscription_plans_max_video_size", "max_video_size_bytes > 0");
                    table.CheckConstraint("ck_subscription_plans_price", "price_amount >= 0");
                    table.CheckConstraint("ck_subscription_plans_scan_limit", "scan_limit >= 0");
                    table.CheckConstraint("ck_subscription_plans_validity_days", "validity_days IS NULL OR validity_days > 0");
                });

            migrationBuilder.CreateTable(
                name: "account_device_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    device_identity_id = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_device_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_device_links_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_account_device_links_device_identities_device_identity_id",
                        column: x => x.device_identity_id,
                        principalTable: "device_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "free_trial_device_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_identity_id = table.Column<long>(type: "bigint", nullable: false),
                    allocated_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    consumed_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_scans = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_free_trial_device_usages", x => x.id);
                    table.CheckConstraint("ck_free_trial_device_usage_counts", "allocated_scans > 0 AND consumed_scans >= 0 AND reserved_scans >= 0 AND consumed_scans + reserved_scans <= allocated_scans");
                    table.ForeignKey(
                        name: "FK_free_trial_device_usages_device_identities_device_identity_~",
                        column: x => x.device_identity_id,
                        principalTable: "device_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    subscription_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_subscriptions", x => x.id);
                    table.CheckConstraint("ck_user_subscriptions_dates", "expires_at > starts_at");
                    table.CheckConstraint("ck_user_subscriptions_status", "status IN ('Pending', 'Active', 'Expired', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_user_subscriptions_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_subscriptions_subscription_plans_subscription_plan_id",
                        column: x => x.subscription_plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    subscription_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    user_subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    order_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    provider_transaction_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    gateway_request_json = table.Column<string>(type: "text", nullable: true),
                    gateway_response_json = table.Column<string>(type: "text", nullable: true),
                    callback_payload_json = table.Column<string>(type: "text", nullable: true),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                    table.CheckConstraint("ck_payment_transactions_amount", "amount >= 0");
                    table.CheckConstraint("ck_payment_transactions_status", "status IN ('Pending', 'Verified', 'Failed', 'Cancelled', 'Expired')");
                    table.ForeignKey(
                        name: "FK_payment_transactions_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_transactions_subscription_plans_subscription_plan_id",
                        column: x => x.subscription_plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_transactions_user_subscriptions_user_subscription_id",
                        column: x => x.user_subscription_id,
                        principalTable: "user_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scan_reservations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    subscription_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    device_identity_id = table.Column<long>(type: "bigint", nullable: true),
                    video_id = table.Column<long>(type: "bigint", nullable: true),
                    analysis_job_id = table.Column<long>(type: "bigint", nullable: true),
                    reservation_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    analysis_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    free_trial_ip_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    release_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_reservations", x => x.id);
                    table.CheckConstraint("ck_scan_reservations_file_size", "file_size_bytes >= 0");
                    table.CheckConstraint("ck_scan_reservations_kind", "reservation_kind IN ('FreeTrial', 'PaidSubscription', 'InternalUnlimited')");
                    table.CheckConstraint("ck_scan_reservations_status", "status IN ('Reserved', 'Consumed', 'Released')");
                    table.ForeignKey(
                        name: "FK_scan_reservations_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scan_reservations_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_reservations_device_identities_device_identity_id",
                        column: x => x.device_identity_id,
                        principalTable: "device_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_reservations_subscription_plans_subscription_plan_id",
                        column: x => x.subscription_plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scan_reservations_user_subscriptions_user_subscription_id",
                        column: x => x.user_subscription_id,
                        principalTable: "user_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_reservations_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scan_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    subscription_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    scan_reservation_id = table.Column<long>(type: "bigint", nullable: true),
                    video_id = table.Column<long>(type: "bigint", nullable: true),
                    analysis_job_id = table.Column<long>(type: "bigint", nullable: true),
                    entitlement_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    analysis_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_usages", x => x.id);
                    table.CheckConstraint("ck_scan_usages_entitlement_type", "entitlement_type IN ('FreeTrial', 'PaidSubscription', 'InternalUnlimited')");
                    table.ForeignKey(
                        name: "FK_scan_usages_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scan_usages_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_usages_scan_reservations_scan_reservation_id",
                        column: x => x.scan_reservation_id,
                        principalTable: "scan_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_usages_subscription_plans_subscription_plan_id",
                        column: x => x.subscription_plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scan_usages_user_subscriptions_user_subscription_id",
                        column: x => x.user_subscription_id,
                        principalTable: "user_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_usages_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "id", "allows_detailed_scan", "allows_smart_scan", "code", "created_at", "currency", "is_active", "max_video_size_bytes", "name", "price_amount", "scan_limit", "sort_order", "updated_at", "validity_days" },
                values: new object[,]
                {
                    { 1L, false, true, "FREE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PKR", true, 209715200L, "Free", 0m, 2, 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 2L, false, true, "PLUS", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PKR", true, 314572800L, "Plus", 499m, 10, 2, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30 },
                    { 3L, true, true, "PRO", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PKR", true, 524288000L, "Pro", 999m, 25, 3, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_device_links_device_last_seen",
                table: "account_device_links",
                columns: new[] { "device_identity_id", "last_seen_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_account_device_links_user_device",
                table: "account_device_links",
                columns: new[] { "user_id", "device_identity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_identities_fingerprint_hash",
                table: "device_identities",
                column: "device_fingerprint_hash");

            migrationBuilder.CreateIndex(
                name: "ix_device_identities_last_seen_at",
                table: "device_identities",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_device_identities_device_token_hash",
                table: "device_identities",
                column: "device_token_hash",
                unique: true,
                filter: "device_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_free_trial_account_usages_user_id",
                table: "free_trial_account_usages",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_free_trial_device_usages_device_id",
                table: "free_trial_device_usages",
                column: "device_identity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_free_trial_ip_usages_ip_hash",
                table: "free_trial_ip_usages",
                column: "ip_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_status",
                table: "payment_transactions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_subscription_id",
                table: "payment_transactions",
                column: "user_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_subscription_plan_id",
                table: "payment_transactions",
                column: "subscription_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_user_status_created",
                table: "payment_transactions",
                columns: new[] { "user_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_order_id",
                table: "payment_transactions",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_transactions_provider_transaction",
                table: "payment_transactions",
                columns: new[] { "provider", "provider_transaction_id" },
                unique: true,
                filter: "provider_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_device_status",
                table: "scan_reservations",
                columns: new[] { "device_identity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_free_trial_ip_hash",
                table: "scan_reservations",
                column: "free_trial_ip_hash");

            migrationBuilder.CreateIndex(
                name: "IX_scan_reservations_subscription_plan_id",
                table: "scan_reservations",
                column: "subscription_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_subscription_status",
                table: "scan_reservations",
                columns: new[] { "user_subscription_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_user_status_reserved",
                table: "scan_reservations",
                columns: new[] { "user_id", "status", "reserved_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_video_id",
                table: "scan_reservations",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ux_scan_reservations_analysis_job_id",
                table: "scan_reservations",
                column: "analysis_job_id",
                unique: true,
                filter: "analysis_job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scan_usages_analysis_job_id",
                table: "scan_usages",
                column: "analysis_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_usages_subscription_occurred",
                table: "scan_usages",
                columns: new[] { "user_subscription_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_scan_usages_subscription_plan_id",
                table: "scan_usages",
                column: "subscription_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_usages_user_occurred",
                table: "scan_usages",
                columns: new[] { "user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_scan_usages_video_id",
                table: "scan_usages",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ux_scan_usages_reservation_id",
                table: "scan_usages",
                column: "scan_reservation_id",
                unique: true,
                filter: "scan_reservation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_active_sort",
                table: "subscription_plans",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_subscription_plans_code",
                table: "subscription_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_expires_at",
                table: "user_subscriptions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_plan_id",
                table: "user_subscriptions",
                column: "subscription_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_user_status_expires",
                table: "user_subscriptions",
                columns: new[] { "user_id", "status", "expires_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_device_links");

            migrationBuilder.DropTable(
                name: "free_trial_account_usages");

            migrationBuilder.DropTable(
                name: "free_trial_device_usages");

            migrationBuilder.DropTable(
                name: "free_trial_ip_usages");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "scan_usages");

            migrationBuilder.DropTable(
                name: "scan_reservations");

            migrationBuilder.DropTable(
                name: "device_identities");

            migrationBuilder.DropTable(
                name: "user_subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_plans");
        }
    }
}
