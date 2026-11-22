# Creates (or reuses) the runner's service account in CISO Assistant and issues it a new personal access token,
# replacing the previous one. Runs INSIDE the backend container through `python manage.py shell`; call it with
# deploy/compose/runner-token.sh, which writes the token to the hound_ca_token secrets file and never prints it.
# Environment: HOUND_SERVICE_EMAIL (the account), HOUND_TOKEN_DAYS (token lifetime, default 365).
import os
from datetime import timedelta

from iam.models import PersonalAccessToken, User, UserGroup
from knox.auth import get_token_model, knox_settings

EMAIL = os.environ["HOUND_SERVICE_EMAIL"]
DAYS = int(os.environ.get("HOUND_TOKEN_DAYS", "365"))
TOKEN_NAME = "evidence-hound-runner"

user = User.objects.filter(email=EMAIL).first() or User.objects.create_user(email=EMAIL)
# hound applies libraries, domains, audits and controls, so the account starts in the built-in administrators
# group. Narrow it with scope/access.yaml once the instance is set up (docs/tutorials/first-deployment.md).
user.user_groups.add(UserGroup.objects.get(name="BI-UG-ADM"))
old = PersonalAccessToken.objects.filter(auth_token__user=user, name=TOKEN_NAME)
get_token_model().objects.filter(pk__in=old.values("auth_token")).delete()
instance, token = get_token_model().objects.create(user=user, expiry=timedelta(days=DAYS), prefix=knox_settings.TOKEN_PREFIX)
PersonalAccessToken.objects.create(name=TOKEN_NAME, auth_token=instance)
print(f"runner-token: issued a {DAYS}-day token for {EMAIL}")
print(f"HOUND_CA_TOKEN={token}")
