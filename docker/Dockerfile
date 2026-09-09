# syntax=docker/dockerfile:1

# Four images from one build: the host application, the provisioning tool, a
# migration runner and the Audit schema deployer. They are separate on purpose.
#
# AGENTS.md section 3 and docs/architecture.md section 4 both insist that schema
# and seed data are applied by something OTHER than the host: separately
# auditable, and eventually separately privileged. A host image that migrated or
# seeded on startup would collapse that distinction, so none of these stages
# lets it.
#
# The Audit schema deployer is separate for a further reason: it runs under a
# credential neither the migrator nor the provisioner may hold. Audit objects
# must be owned by a role nothing can authenticate as, and whoever runs CREATE
# TABLE owns the table -- so the migrator cannot create them without owning,
# and therefore being able to destroy, the trail.
#
# Only the SDK stage ever sees the source. The four runtime images carry
# published output and nothing else.

ARG DOTNET_VERSION=10.0
ARG EF_VERSION=10.0.11


# --------------------------------------------------------------- restore

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /source

# Project files first, so a source-only change does not invalidate the restore
# layer. Test projects are deliberately absent: nothing here runs tests, and
# restoring them would pull the whole xUnit graph into every build.
COPY Ligature.slnx ./
COPY src/Shared/Ligature.SharedKernel/*.csproj                            src/Shared/Ligature.SharedKernel/
COPY src/Platform/Ligature.Platform.Domain/*.csproj                     src/Platform/Ligature.Platform.Domain/
COPY src/Platform/Ligature.Platform.Application/*.csproj                src/Platform/Ligature.Platform.Application/
COPY src/Platform/Ligature.Platform.Persistence/*.csproj                src/Platform/Ligature.Platform.Persistence/
COPY src/Host/Ligature.Host/*.csproj                                    src/Host/Ligature.Host/
COPY src/Tools/Ligature.Provisioning/*.csproj                           src/Tools/Ligature.Provisioning/
COPY src/Tools/Ligature.AuditSchema/*.csproj                            src/Tools/Ligature.AuditSchema/

RUN dotnet restore src/Host/Ligature.Host/Ligature.Host.csproj \
 && dotnet restore src/Tools/Ligature.Provisioning/Ligature.Provisioning.csproj \
 && dotnet restore src/Tools/Ligature.AuditSchema/Ligature.AuditSchema.csproj

COPY src/ src/

RUN dotnet publish src/Host/Ligature.Host/Ligature.Host.csproj \
        --configuration Release --no-restore --output /out/host

RUN dotnet publish src/Tools/Ligature.Provisioning/Ligature.Provisioning.csproj \
        --configuration Release --no-restore --output /out/provisioner

RUN dotnet publish src/Tools/Ligature.AuditSchema/Ligature.AuditSchema.csproj \
        --configuration Release --no-restore --output /out/audit-schema


# ------------------------------------------------------------ migrations

# `dotnet ef migrations bundle` produces a standalone executable that applies
# the migrations and exits. It is used instead of shipping the SDK so the
# migration image is a runtime image like the other two, and instead of
# Database.Migrate() on startup so the host keeps no schema authority.
#
# LigatureDbContextFactory is the design-time factory, so --project stands alone
# and there is no --startup-project (AGENTS.md section 3).
FROM build AS migrations
ARG EF_VERSION
ENV PATH="${PATH}:/root/.dotnet/tools"

# Building the bundle instantiates LigatureDbContextFactory, which requires
# LIGATURE_CONNECTION and has no fallback — deliberately, because a fallback
# silently migrated whichever database the default named.
#
# A bundle needs the MODEL, not a database: nothing here connects. So the value
# below is a deliberately unresolvable placeholder that exists only to satisfy
# construction, and it cannot reintroduce the defect it looks like it might.
# The real connection arrives at RUN time, via --connection in the migrator
# entrypoint; if this placeholder were ever used it would fail to resolve
# rather than reach a real server.
ENV LIGATURE_CONNECTION="Host=ef-bundle-build-only.invalid;Database=none;Username=none;Password=none"

RUN dotnet tool install --global dotnet-ef --version ${EF_VERSION}

RUN dotnet ef migrations bundle \
        --project src/Platform/Ligature.Platform.Persistence \
        --configuration Release \
        --force \
        --output /out/migrator/efbundle


# ----------------------------------------------------------------- host

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS host

# Npgsql probes for GSSAPI/Kerberos when it opens a connection. The .NET runtime
# images do not carry it, so without this every connection logs
# "Cannot load library libgssapi_krb5.so.2" as an Error. Password authentication
# works either way; this exists so the logs do not cry wolf.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out/host ./

# Kestrel's in-container default. Nothing in the app hard-codes it; compose
# publishes it, and ASPNETCORE_HTTP_PORTS can move it.
EXPOSE 8080

# Non-root. The image provides this account; the app needs no write access to
# anything in the filesystem.
USER $APP_UID

ENTRYPOINT ["dotnet", "Ligature.Host.dll"]


# ------------------------------------------------------------- migrator

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS migrator

# Npgsql probes for GSSAPI/Kerberos when it opens a connection. The .NET runtime
# images do not carry it, so without this every connection logs
# "Cannot load library libgssapi_krb5.so.2" as an Error. Password authentication
# works either way; this exists so the logs do not cry wolf.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=migrations /out/migrator/ ./

USER $APP_UID

# The bundle takes the connection as an argument rather than configuration, so
# the shell form expands LIGATURE_CONNECTION — the same variable every other
# component reads, rather than a second name for the same thing.
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$LIGATURE_CONNECTION\""]


# ---------------------------------------------------------- provisioner

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS provisioner

# Npgsql probes for GSSAPI/Kerberos when it opens a connection. The .NET runtime
# images do not carry it, so without this every connection logs
# "Cannot load library libgssapi_krb5.so.2" as an Error. Password authentication
# works either way; this exists so the logs do not cry wolf.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out/provisioner ./

# Writes the bootstrap activation token owner-only, so it must own the
# directory it writes into. Compose mounts one and this user is its owner.
USER $APP_UID

ENTRYPOINT ["dotnet", "Ligature.Provisioning.dll"]


# ------------------------------------------------------------ audit-schema

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS audit-schema

# Npgsql probes for GSSAPI/Kerberos when it opens a connection. The .NET runtime
# images do not carry it, so without this every connection logs
# "Cannot load library libgssapi_krb5.so.2" as an Error. Password authentication
# works either way; this exists so the logs do not cry wolf.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out/audit-schema ./

USER $APP_UID

# Runs AFTER the migrator: the Audit foreign keys reference app_user, role and
# user_role (AR10, AR12), so the User Management schema must already exist.
#
# LIGATURE_PRIVILEGED_CONNECTION, not LIGATURE_CONNECTION. This step needs
# authority to create a schema owned by another role, which is deliberately not
# what the application or the migrator connects with.
ENTRYPOINT ["dotnet", "Ligature.AuditSchema.dll"]
