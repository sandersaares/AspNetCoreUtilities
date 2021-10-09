# Fast File Exchange

Implements an ASP.NET Core request handler that enables files to be exchanged between apps with minimal delay.

API `/files/<any path>`:

* POST will create (a version of) a file.
* GET will start downloading the latest version of the file, even if the upload has not finished yet.
* DELETE will delete the latest version of the file.

API `/diagnostics`:

* GET will show a dump of the current state.

Expiration control:

* Files expire when not accessed for 60 seconds.
* You can define regex patterns to define a custom expiration for specific files.

# Ideas for enhancements

Unlikely to be needed, mainly just to highlight important gaps:

* Eagerly kick out stalled connections that do not transfer any data for N seconds.
* Limit the total allowed memory consumption to survive upload floods.