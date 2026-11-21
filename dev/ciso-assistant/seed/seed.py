# Seeds a disposable CISO Assistant through `python manage.py shell` (see seed.sh).
# Creates a service-account user with a personal access token, loads the Essential Eight (November 2023),
# the ASD Essentials for enterprise IT, the transition mapping between them and the fictional Example
# baseline libraries, and creates the Example Pty Ltd folder, perimeter, audits and applied controls
# through the REST API. Everything here is fictional.
import json
import os
import urllib.error
import urllib.request
from datetime import timedelta

from iam.models import PersonalAccessToken, User, UserGroup
from knox.auth import get_token_model, knox_settings

API = os.environ.get("SEED_API_URL", "http://localhost:8000/api")
SERVICE_EMAIL = "hound-service@example.com"
TOKEN_NAME = "evidence-hound"
LIBRARIES = [
    "urn:intuitem:risk:library:essential-eight-2023",
    "urn:intuitem:risk:library:asd-essentials-enterprise-it",
    "urn:intuitem:risk:library:mapping-essential-eight-2023-and-asd-essentials-enterprise-it",
    "urn:example:risk:library:example-baseline",
]
FOLDER = "Example Pty Ltd"
PERIMETER = "Example baseline"
AUDIT_REF_ID = "EXAMPLE-BASELINE-2026"
# ref_id: (name, framework URN, selected implementation groups)
AUDITS = {
    AUDIT_REF_ID: ("Example baseline 2026", "urn:example:risk:framework:example-baseline", []),
    "EX-E8-2026": ("Example Essential Eight ML2 2026", "urn:intuitem:risk:framework:essential-eight-2023", ["1", "2"]),
    "EX-ESSENTIALS-2026": ("Example Essentials phase 1 2026", "urn:intuitem:risk:framework:asd-essentials-enterprise-it", ["1"]),
}
CONTROLS = {
    "AC-MFA": "Multi-factor authentication",
    "AC-BACKUP": "Daily backups",
    "AC-AUTO-EVIDENCE": "Automated evidence collection",
}


def service_token():
    user = User.objects.filter(email=SERVICE_EMAIL).first() or User.objects.create_user(email=SERVICE_EMAIL)
    user.user_groups.add(UserGroup.objects.get(name="BI-UG-ADM"))
    old = PersonalAccessToken.objects.filter(auth_token__user=user, name=TOKEN_NAME)
    get_token_model().objects.filter(pk__in=old.values("auth_token")).delete()
    instance, token = get_token_model().objects.create(user=user, expiry=timedelta(days=30), prefix=knox_settings.TOKEN_PREFIX)
    PersonalAccessToken.objects.create(name=TOKEN_NAME, auth_token=instance)
    return token


def call(token, method, path, body=None):
    request = urllib.request.Request(
        API + path,
        data=None if body is None else json.dumps(body).encode(),
        method=method,
        headers={"Authorization": f"Token {token}", "Content-Type": "application/json", "Accept": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode(errors="replace")


def listed(token, path):
    status, body = call(token, "GET", f"{path}?limit=1000")
    if status != 200:
        raise SystemExit(f"seed: GET {path} returned {status}: {body}")
    return body["results"] if isinstance(body, dict) else body


def find(token, path, **match):
    return next((o for o in listed(token, path) if all(o.get(k) == v for k, v in match.items())), None)


def ensure(token, path, match, body):
    existing = find(token, path, **match)
    if existing:
        return existing["id"]
    status, created = call(token, "POST", path, body)
    if status not in (200, 201):
        raise SystemExit(f"seed: POST {path} returned {status}: {created}")
    return created["id"]


token = service_token()
for urn in LIBRARIES:
    status, body = call(token, "POST", f"/stored-libraries/{urn}/import/")
    if status != 200:
        print(f"seed: library {urn} not imported ({status}: {body}); it may already be loaded")

folder = ensure(token, "/folders/", {"name": FOLDER}, {"name": FOLDER})
perimeter = ensure(token, "/perimeters/", {"name": PERIMETER}, {"name": PERIMETER, "folder": folder})
for ref_id, (name, framework_urn, groups) in AUDITS.items():
    framework = find(token, "/frameworks/", urn=framework_urn)
    if framework is None:
        raise SystemExit(f"seed: framework {framework_urn} is not loaded")
    audit = {"name": name, "ref_id": ref_id, "perimeter": perimeter, "framework": framework["id"], "folder": folder}
    if groups:
        audit["selected_implementation_groups"] = groups
    ensure(token, "/compliance-assessments/", {"ref_id": ref_id}, audit)
for ref_id, name in CONTROLS.items():
    ensure(token, "/applied-controls/", {"ref_id": ref_id}, {"name": name, "ref_id": ref_id, "folder": folder, "status": "to_do"})

print(f"seed: {FOLDER}, audits {', '.join(AUDITS)} and controls {', '.join(CONTROLS)} are ready")
print(f"HOUND_CA_TOKEN={token}")
