using System.CommandLine;
using System.Globalization;
using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Setup;

/// <summary>
/// <c>hound add system|control|collector</c>: asks what to add and adds it to the compliance repo's files in place. Each change is checked
/// the way <c>scope validate</c> and <c>config validate</c> check the files before anything is written; a change that would not validate
/// is not written.
/// </summary>
internal static class AddCommands
{
    private static readonly string DefaultSchedule = "0 3 * * *";

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.add");
        return new Command("add", "Add a system, a control or a collector to a compliance repo, asking for what it needs.")
        {
            SystemCommand(terminal, logger, configuration, registry),
            ControlCommand(terminal, logger, configuration, registry),
            CollectorCommand(terminal, logger, registry),
        };
    }

    private static Option<DirectoryInfo> ScopeOption() => new("--scope") { Description = "The scope folder (holding setup.yaml).", Required = true };

    private static Option<bool> YesOption() => new("--yes", "-y") { Description = "Ask nothing: take the options given and the defaults." };

    private static Command SystemCommand(Terminal terminal, ILogger logger, IConfiguration configuration, CollectorRegistry registry)
    {
        var scope = ScopeOption();
        var yes = YesOption();
        var domain = new Option<string?>("--domain") { Description = "The domain the system belongs to. Asked only when the scope has more than one." };
        var name = new Option<string?>("--name") { Description = "The system's name, as audits and controls name it." };
        var refId = new Option<string?>("--ref-id") { Description = "The system's ref ID. Default: SYS- and the name." };
        var type = new Option<string?>("--type") { Description = "primary (holds the business process or data) or supporting. Default: supporting." };
        var owner = new Option<string?>("--owner") { Description = "The email of the system's owner." };
        var description = new Option<string?>("--description") { Description = "What the system is." };
        var parents = new Option<string?>("--parents") { Description = "Comma-separated systems this one supports." };
        var audits = new Option<string[]>("--audit") { Description = "An audit (by ref_id) to add the system to. Repeat for more.", AllowMultipleArgumentsPerToken = false };

        var command = new Command("system", "Add a system to a domain in scope/setup.yaml, and to audits named with --audit.")
        {
            scope, domain, name, refId, type, owner, description, parents, audits, yes,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var folder = parseResult.GetRequiredValue(scope);
            var today = AuditSeries.Today(configuration);
            var current = await RepoScope.ValidateAsync(folder, null, registry, today, cancellationToken);
            if (current.Problems.Count > 0)
            {
                return Log.Problems(logger, current.Problems.Select(p => p.ToProblem()));
            }

            var wizard = new Wizard(terminal, parseResult.GetValue(yes));
            var domains = current.Scope.Setup.Domains;
            var domainName = await AskDomainAsync(wizard, domains, parseResult.GetValue(domain), cancellationToken);
            var systemName = await wizard.AskAsync("System name", parseResult.GetValue(name), "", cancellationToken);
            var systemRef = await wizard.AskRefIdAsync(RefIdKind.System, "System ref_id", parseResult.GetValue(refId), Wizard.Slug(systemName), cancellationToken);
            var system = YamlMap.Of(
                ("name", new YamlText(systemName)),
                ("ref_id", new YamlText(systemRef.Match(r => r, _ => ""))),
                ("type", new YamlText(await wizard.AskAsync("Type (primary or supporting)", parseResult.GetValue(type), "supporting", cancellationToken))),
                ("owner", new YamlText(await wizard.AskAsync("Owner's email, as a CISO Assistant user", parseResult.GetValue(owner), "", cancellationToken))),
                ("description", new YamlText(await wizard.AskAsync("Description", parseResult.GetValue(description), "", cancellationToken))),
                ("parents", new YamlFlowList(Wizard.List(await wizard.AskAsync("Systems it supports, comma-separated", parseResult.GetValue(parents), "", cancellationToken)))));
            system = new YamlMap([.. Ordered(system, "name", "ref_id", "type", "description", "owner", "parents")]);

            var problems = new List<Problem>(systemRef.Match(_ => [], p => p));
            if (systemName.Length == 0)
            {
                problems.Add(new Problem("add.answer_invalid", "The system name is empty."));
            }

            if (domains.SelectMany(d => d.Systems).Any(s => s.Name == systemName))
            {
                problems.Add(new Problem("add.system_exists", $"A system named '{systemName}' is already declared in setup.yaml."));
            }

            if (problems.Count > 0)
            {
                return Log.Problems(logger, problems);
            }

            var setupPath = Path.GetFullPath(Path.Combine(folder.FullName, "setup.yaml"));
            var edits = new Dictionary<string, string>(StringComparer.Ordinal);
            var edited = (await EditAsync(edits, setupPath, text => YamlEdit.AppendItem(text, $"domains[name={domainName}].systems", system), cancellationToken))
                .Bind(_ => Combine(parseResult.GetValue(audits) ?? [], refIdOfAudit => AuditFile(current.Scope, refIdOfAudit)))
                .BindAsync(files => EditAllAsync(edits, files, text => YamlEdit.AppendScalar(text, "systems", systemName), cancellationToken));
            return await (await edited).Match(
                _ => CheckAndWriteScopeAsync(folder, edits, registry, today, terminal, logger, $"Added system {systemName} to {domainName}", cancellationToken),
                p => Task.FromResult(Log.Problems(logger, p)));
        });

        return command;
    }

    private static Command ControlCommand(Terminal terminal, ILogger logger, IConfiguration configuration, CollectorRegistry registry)
    {
        var scope = ScopeOption();
        var yes = YesOption();
        var refId = new Option<string?>("--ref-id") { Description = "The control's ref ID, such as AC-BACKUP." };
        var name = new Option<string?>("--name") { Description = "What the control is." };
        var domain = new Option<string?>("--domain") { Description = "The domain that owns it. Asked only when the scope has more than one." };
        var owner = new Option<string?>("--owner") { Description = "The email of the control's owner." };
        var category = new Option<string?>("--category") { Description = "policy, process, technical, physical or procedure. Default: technical." };
        var systems = new Option<string?>("--systems") { Description = "Comma-separated systems it covers, by name or ref ID (written by name)." };
        var audit = new Option<string?>("--audit") { Description = "An audit (by ref_id) to link the control to." };
        var requirements = new Option<string?>("--requirements") { Description = "Comma-separated requirements of that audit's framework the control supports, such as ISM-1511." };

        var command = new Command("control", "Declare an applied control in scope/controls.yaml, and link it to an audit's requirements with --audit.")
        {
            scope, refId, name, domain, owner, category, systems, audit, requirements, yes,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var folder = parseResult.GetRequiredValue(scope);
            var today = AuditSeries.Today(configuration);
            var current = await RepoScope.ValidateAsync(folder, null, registry, today, cancellationToken);
            if (current.Problems.Count > 0)
            {
                return Log.Problems(logger, current.Problems.Select(p => p.ToProblem()));
            }

            var wizard = new Wizard(terminal, parseResult.GetValue(yes));
            var domains = current.Scope.Setup.Domains;
            var askedRef = await wizard.AskRefIdAsync(RefIdKind.Control, "Control ref_id", parseResult.GetValue(refId), "", cancellationToken);
            var controlRef = askedRef.Match(r => r, _ => "");
            var controlName = await wizard.AskAsync("Control name", parseResult.GetValue(name), "", cancellationToken);
            var domainName = await AskDomainAsync(wizard, domains, parseResult.GetValue(domain), cancellationToken);
            var control = YamlMap.Of(
                ("ref_id", new YamlText(controlRef)),
                ("name", new YamlText(controlName)),
                ("domain", new YamlText(domainName)),
                ("owner", new YamlText(await wizard.AskAsync("Owner's email, as a CISO Assistant user", parseResult.GetValue(owner), "", cancellationToken))),
                ("status", new YamlText("to_do")),
                ("category", new YamlText(await wizard.AskAsync("Category (policy, process, technical, physical or procedure)", parseResult.GetValue(category), "technical", cancellationToken))),
                ("systems", new YamlFlowList(SystemNames(domains, Wizard.List(await wizard.AskAsync("Systems it covers, comma-separated", parseResult.GetValue(systems), "", cancellationToken))))));
            var auditRef = parseResult.GetValue(audit);
            var linked = auditRef is null ? [] : Wizard.List(await wizard.AskAsync($"Requirements of {auditRef} it supports, comma-separated", parseResult.GetValue(requirements), "", cancellationToken));

            var problems = new List<Problem>(askedRef.Match(_ => [], p => p));
            if (controlName.Length == 0)
            {
                problems.Add(new Problem("add.answer_invalid", "The control needs a ref ID and a name."));
            }

            if (current.Scope.Controls.Any(c => c.RefId == controlRef))
            {
                problems.Add(new Problem("add.control_exists", $"A control with ref ID '{controlRef}' is already declared in controls.yaml."));
            }

            if (problems.Count > 0)
            {
                return Log.Problems(logger, problems);
            }

            var controlsPath = Path.GetFullPath(Path.Combine(folder.FullName, "controls.yaml"));
            var edits = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(controlsPath))
            {
                edits[controlsPath] = "# Applied controls, declared once and used by any number of audits. See docs/scope.md.\ncontrols: []\n";
            }

            var edited = (await EditAsync(edits, controlsPath, text => YamlEdit.AppendItem(text, "controls", control), cancellationToken))
                .Bind(_ => auditRef is null ? Result.Ok<IReadOnlyList<string>>([]) : AuditFile(current.Scope, auditRef).Map(file => (IReadOnlyList<string>)[file]))
                .BindAsync(files => EditAllAsync(
                    edits,
                    files,
                    text => YamlEdit.AppendItem(text, "controls", YamlMap.Of(("ref_id", new YamlText(controlRef)), ("requirements", new YamlFlowList(linked)))),
                    cancellationToken));
            return await (await edited).Match(
                _ => CheckAndWriteScopeAsync(folder, edits, registry, today, terminal, logger, $"Added control {controlRef}", cancellationToken),
                p => Task.FromResult(Log.Problems(logger, p)));
        });

        return command;
    }

    /// <summary>Each system given by its ref ID, such as SYS-365, as its name; names and unknown entries as given, for validation to judge.</summary>
    private static IReadOnlyList<string> SystemNames(IReadOnlyList<ScopeDomain> domains, IReadOnlyList<string> given)
    {
        var byRef = domains.SelectMany(d => d.Systems).Where(s => s.RefId is not null).ToDictionary(s => s.RefId!, s => s.Name, StringComparer.Ordinal);
        return [.. given.Select(g => byRef.GetValueOrDefault(g, g))];
    }

    private static Command CollectorCommand(Terminal terminal, ILogger logger, CollectorRegistry registry)
    {
        var type = new Argument<string>("type") { Description = "The collector type, as hound collectors lists it, such as manual or rest." };
        var config = new Option<FileInfo>("--config") { Description = "The deployment config file (YAML).", Required = true };
        var name = new Option<string?>("--name") { Description = "The collector's name in the config. Default: the type." };
        var schedule = new Option<string?>("--schedule") { Description = $"When it runs, as a cron expression. Default: {DefaultSchedule}." };
        var set = new Option<string[]>("--set") { Description = "A setting, as key=value. Repeat for more; optional settings are set only this way." };
        var secretsDir = new Option<DirectoryInfo?>("--secrets-dir") { Description = "Read each secret's value and store it in this folder (mode 700, files mode 600), referenced as file:. Otherwise each secret is asked as a reference." };
        var secretFile = new Option<string[]>("--secret-file") { Description = "A secret from a file, as secret=path, such as client_certificate=app.pem: copied into --secrets-dir and referenced there. Repeat for more; optional secrets are set only this way." };
        var yes = YesOption();

        var command = new Command("collector", "Add a collector to the deployment config, asking for its schedule, required settings and secrets.")
        {
            type, config, name, schedule, set, secretsDir, secretFile, yes,
        };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var typeName = parseResult.GetRequiredValue(type);
            if (!registry.Descriptions.TryGetValue(typeName, out var described))
            {
                return Log.Problems(logger, [new Problem("add.collector_type_unknown", $"unknown collector type '{typeName}'; known types: {string.Join(", ", registry.Descriptions.Keys.Order(StringComparer.Ordinal))}.")]);
            }

            var given = Settings(parseResult.GetValue(set) ?? []);
            if (given is Result<IReadOnlyList<KeyValuePair<string, string>>>.Fail badSet)
            {
                return Log.Problems(logger, badSet.Errors);
            }

            var store = parseResult.GetValue(secretsDir);
            var configFile = parseResult.GetRequiredValue(config);
            var wizard = new Wizard(terminal, parseResult.GetValue(yes));
            var collectorName = await wizard.AskAsync("Collector name", parseResult.GetValue(name), typeName, cancellationToken);
            var when = await wizard.AskAsync("Schedule (cron)", parseResult.GetValue(schedule), DefaultSchedule, cancellationToken);
            var settings = await AskSettingsAsync(wizard, described, given.Match(g => g, _ => []), cancellationToken);
            if (described.SettingsFrom is { } settingsFrom)
            {
                described = settingsFrom(new CollectorSettings(settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal)));
                settings = await AskSettingsAsync(wizard, described, settings, cancellationToken);
            }

            var fromFiles = SecretFiles(parseResult.GetValue(secretFile) ?? [], described, store);
            if (fromFiles is Result<IReadOnlyList<KeyValuePair<string, string>>>.Fail badFile)
            {
                return Log.Problems(logger, badFile.Errors);
            }

            var files = fromFiles.Match(f => f, _ => []);
            var secrets = new List<(SettingDescription Secret, string Reference, string? Value)>();
            foreach (var (secretName, path) in files)
            {
                var secret = described.Secrets.First(s => s.Name == secretName);
                secrets.Add((secret, $"file:{Path.Combine(Path.GetFullPath(store!.FullName), $"{collectorName}_{secretName}")}", await File.ReadAllTextAsync(path, cancellationToken)));
            }

            foreach (var secret in described.Secrets.Where(s => s.Required && files.All(f => f.Key != s.Name)))
            {
                if (store is null)
                {
                    secrets.Add((secret, await wizard.AskAsync($"{secret.Description} Reference for {secret.Name}", null, $"file:/run/secrets/{collectorName}_{secret.Name}", cancellationToken), null));
                    continue;
                }

                var value = await wizard.AskSecretAsync($"{secret.Description} ({secret.Name})", cancellationToken);
                if (value.Length == 0)
                {
                    return Log.Problems(logger, [new Problem("add.secret_empty", $"No value was given for {secret.Name}.")]);
                }

                secrets.Add((secret, $"file:{Path.Combine(Path.GetFullPath(store.FullName), $"{collectorName}_{secret.Name}")}", value));
            }

            var collector = YamlMap.Of(
                ("name", new YamlText(collectorName)),
                ("type", new YamlText(typeName)),
                ("schedule", new YamlText(when)),
                ("settings", new YamlMap([.. settings.Select(s => KeyValuePair.Create(s.Key, (YamlValue)new YamlText(s.Value)))])),
                ("secrets", new YamlMap([.. secrets.Select(s => KeyValuePair.Create(s.Secret.Name, (YamlValue)new YamlText(s.Reference)))])));

            var text = configFile.Exists ? await File.ReadAllTextAsync(configFile.FullName, cancellationToken) : "";
            var checkedConfig = YamlEdit.AppendItem(text, "collectors", collector)
                .Bind(edited => ConfigLoader.Parse(edited, configFile.FullName, registry.Descriptions).Map(_ => edited));
            return await checkedConfig.Match(
                async edited =>
                {
                    foreach (var (_, reference, value) in secrets.Where(s => s.Value is not null))
                    {
                        await WriteSecretAsync(reference["file:".Length..], value!, cancellationToken);
                    }

                    await File.WriteAllTextAsync(configFile.FullName, edited, cancellationToken);
                    await terminal.Output.WriteLineAsync($"Added collector {collectorName} ({typeName}) to {configFile.FullName}.");
                    await terminal.Output.WriteLineAsync(
                        $"Next: hound config validate {configFile.Name}, then hound run --config {configFile.Name} --only {collectorName} --dry-run. "
                        + "Add --check-secrets once its secrets are set (and CISO Assistant's token).");
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        return command;
    }

    /// <summary>Each required setting, given or asked, in the collector's own order; then the other settings given with --set.</summary>
    private static async Task<IReadOnlyList<KeyValuePair<string, string>>> AskSettingsAsync(
        Wizard wizard, CollectorDescription described, IReadOnlyList<KeyValuePair<string, string>> given, CancellationToken cancellationToken)
    {
        var settings = new List<KeyValuePair<string, string>>();
        foreach (var setting in described.Settings)
        {
            var value = given.FirstOrDefault(g => g.Key == setting.Name).Value;
            if (value is null && setting.Required)
            {
                value = await wizard.AskAsync($"{setting.Description} ({setting.Name})", null, "", cancellationToken);
            }

            if (value is { Length: > 0 })
            {
                settings.Add(KeyValuePair.Create(setting.Name, value));
            }
        }

        settings.AddRange(given.Where(g => described.Settings.All(s => s.Name != g.Key)));
        return settings;
    }

    /// <summary>
    /// The secrets to read from files, as secret name and path: each must be a secret the collector has, name a file that exists, and come with
    /// --secrets-dir, where it is copied.
    /// </summary>
    private static Result<IReadOnlyList<KeyValuePair<string, string>>> SecretFiles(IEnumerable<string> pairs, CollectorDescription described, DirectoryInfo? store)
    {
        var given = pairs.Select(pair => pair.Split('=', 2)).ToArray();
        if (given.Length == 0)
        {
            return Result.Ok<IReadOnlyList<KeyValuePair<string, string>>>([]);
        }

        if (store is null)
        {
            return Result.Fail<IReadOnlyList<KeyValuePair<string, string>>>(new Problem("add.secret_file_without_secrets_dir", "--secret-file copies the file into the secrets folder: give --secrets-dir too."));
        }

        var problems = new List<Problem>();
        foreach (var pair in given)
        {
            if (pair.Length != 2 || pair[0].Trim().Length == 0)
            {
                problems.Add(new Problem("add.secret_file_invalid", $"--secret-file {string.Join('=', pair)} must be secret=path."));
            }
            else if (described.Secrets.All(s => s.Name != pair[0].Trim()))
            {
                problems.Add(new Problem("add.secret_unknown", $"'{pair[0].Trim()}' is not a secret of this collector; it has: {string.Join(", ", described.Secrets.Select(s => s.Name))}."));
            }
            else if (!File.Exists(pair[1].Trim()))
            {
                problems.Add(new Problem("add.secret_file_missing", $"{pair[1].Trim()} does not exist."));
            }
        }

        return problems.Count > 0
            ? Result.Fail<IReadOnlyList<KeyValuePair<string, string>>>([.. problems])
            : Result.Ok<IReadOnlyList<KeyValuePair<string, string>>>([.. given.Select(p => KeyValuePair.Create(p[0].Trim(), p[1].Trim()))]);
    }

    private static Result<IReadOnlyList<KeyValuePair<string, string>>> Settings(IEnumerable<string> pairs)
    {
        var parsed = pairs.Select(pair => pair.Split('=', 2)).ToArray();
        return parsed.FirstOrDefault(p => p.Length != 2 || p[0].Trim().Length == 0) is { } bad
            ? Result.Fail<IReadOnlyList<KeyValuePair<string, string>>>(new Problem("add.set_invalid", $"--set {string.Join('=', bad)} must be key=value."))
            : Result.Ok<IReadOnlyList<KeyValuePair<string, string>>>([.. parsed.Select(p => KeyValuePair.Create(p[0].Trim(), p[1].Trim()))]);
    }

    /// <summary>Writes a secret's value to its file, mode 600 in a folder created mode 700. The value is never logged or printed.</summary>
    private static async Task WriteSecretAsync(string path, string value, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(path, options))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(value.AsMemory(), cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>The domain to add to: the one given, the only one, or the one the person picks.</summary>
    private static async Task<string> AskDomainAsync(Wizard wizard, IReadOnlyList<ScopeDomain> domains, string? given, CancellationToken cancellationToken) =>
        given ?? (domains.Count == 1
            ? domains[0].Name
            : await wizard.AskAsync($"Domain ({string.Join(", ", domains.Select(d => d.Name))})", null, domains.Count > 0 ? domains[0].Name : "", cancellationToken));

    private static IEnumerable<KeyValuePair<string, YamlValue>> Ordered(YamlMap map, params string[] keys) =>
        keys.SelectMany(key => map.Entries.Where(e => e.Key == key));

    private static Result<string> AuditFile(ScopeDocument scope, string refId) =>
        scope.Audits.FirstOrDefault(a => a.RefId == refId) is { } audit
            ? Result.Ok(Path.GetFullPath(audit.At.File))
            : Result.Fail<string>(new Problem("add.audit_unknown", $"No audit with ref_id '{refId}' in the scope's audits folder. Audits: {string.Join(", ", scope.Audits.Select(a => a.RefId))}."));

    private static Result<IReadOnlyList<string>> Combine(IEnumerable<string> items, Func<string, Result<string>> each) =>
        Result.Combine(items.Select(each));

    /// <summary>Edits one file in <paramref name="edits"/>, starting from its pending text or from disk.</summary>
    private static async Task<Result<string>> EditAsync(Dictionary<string, string> edits, string path, Func<string, Result<string>> edit, CancellationToken cancellationToken)
    {
        var text = edits.TryGetValue(path, out var pending) ? pending : await File.ReadAllTextAsync(path, cancellationToken);
        return edit(text).Match(
            edited =>
            {
                edits[path] = edited;
                return Result.Ok(edited);
            },
            problems => Result.Fail<string>(problems.Select(p => new Problem(p.Code, $"{path}: {p.Message}"))));
    }

    private static async Task<Result<IReadOnlyList<string>>> EditAllAsync(
        Dictionary<string, string> edits, IReadOnlyList<string> paths, Func<string, Result<string>> edit, CancellationToken cancellationToken)
    {
        var results = new List<Result<string>>();
        foreach (var path in paths)
        {
            results.Add(await EditAsync(edits, path, edit, cancellationToken));
        }

        return Result.Combine(results);
    }

    /// <summary>Checks the scope with the edits in place of its files, then writes them; a change that would not validate is not written.</summary>
    private static async Task<int> CheckAndWriteScopeAsync(
        DirectoryInfo folder, IReadOnlyDictionary<string, string> edits, CollectorRegistry registry, DateOnly today, Terminal terminal, ILogger logger, string done, CancellationToken cancellationToken)
    {
        var checkedScope = await RepoScope.ValidateAsync(folder, null, registry, today, cancellationToken, edits);
        if (checkedScope.Problems.Count > 0)
        {
            return Log.Problems(logger, checkedScope.Problems.Select(p => p.ToProblem()));
        }

        foreach (var (path, text) in edits)
        {
            await File.WriteAllTextAsync(path, text, cancellationToken);
        }

        await terminal.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{done}: {string.Join(", ", edits.Keys.Select(Path.GetFileName))}."));
        return ExitCodes.Ok;
    }
}
