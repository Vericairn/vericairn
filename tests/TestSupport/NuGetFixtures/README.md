# NuGet V3 response fixtures

These small, offline response bodies preserve the public V3 service-index,
package-content version-index and registration shapes used by the provider.
Package names are examples. They are protocol examples retained for replay,
not a claim about the live state of those names. No test contacts nuget.org.

`listed`, `unlisted`, `deprecated`, `absent`, `not-found`, `malformed`, and
the paged pair cover registry states. `oversized` and `timeout` describe
transport failures replayed by the test handler; a timeout has no response
body and therefore produces no response digest or observation.
