# Clean-room reference policy

The root Go agent and the Perl projects under `../base/` may be observed to
derive requirements, protocol fixtures, category names, and expected behavior.
GPL-covered implementation text must not be translated or copied into this
project. New code is written from public Windows and GLPI documentation,
protocol schemas, sanitized observations, and independently designed tests.

Fixture data must contain no credentials, production hostnames, serial numbers,
user names, or other identifying endpoint data.
