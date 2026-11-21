# Seeds a disposable CISO Assistant through `python manage.py shell` (see seed.sh).
# Creates a service-account user with a personal access token, and the example staff users the example scope names as owners,
# authors and reviewers. Everything else (libraries, folder, perimeter, audits, applied controls) comes from
# `hound scope apply examples/compliance-repo/scope`, which runs after this. Everything here is fictional.
from datetime import timedelta

from iam.models import PersonalAccessToken, User, UserGroup
from knox.auth import get_token_model, knox_settings

SERVICE_EMAIL = "hound-service@example.com"
# People the example scope names as owners, authors and reviewers. Users have no REST endpoint to create them without an invitation email.
STAFF = ["it@example.com", "ceo@example.com"]
TOKEN_NAME = "evidence-hound"


def service_token():
    user = User.objects.filter(email=SERVICE_EMAIL).first() or User.objects.create_user(email=SERVICE_EMAIL)
    user.user_groups.add(UserGroup.objects.get(name="BI-UG-ADM"))
    old = PersonalAccessToken.objects.filter(auth_token__user=user, name=TOKEN_NAME)
    get_token_model().objects.filter(pk__in=old.values("auth_token")).delete()
    instance, token = get_token_model().objects.create(user=user, expiry=timedelta(days=30), prefix=knox_settings.TOKEN_PREFIX)
    PersonalAccessToken.objects.create(name=TOKEN_NAME, auth_token=instance)
    return token


token = service_token()
for email in STAFF:
    if not User.objects.filter(email=email).exists():
        User.objects.create_user(email=email)

print(f"seed: service account {SERVICE_EMAIL} and users {', '.join(STAFF)} are ready; run hound scope apply next")
print(f"HOUND_CA_TOKEN={token}")
