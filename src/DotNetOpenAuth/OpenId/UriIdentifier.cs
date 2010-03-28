﻿//-----------------------------------------------------------------------
// <copyright file="UriIdentifier.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.OpenId {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
	using System.Diagnostics.Contracts;
	using System.Linq;
	using System.Reflection;
	using System.Security;
	using System.Text.RegularExpressions;
	using System.Web.UI.HtmlControls;
	using System.Xml;
	using DotNetOpenAuth.Messaging;
	using DotNetOpenAuth.OpenId.RelyingParty;
	using DotNetOpenAuth.Xrds;
	using DotNetOpenAuth.Yadis;

	/// <summary>
	/// A URI style of OpenID Identifier.
	/// </summary>
	[Serializable]
	[Pure]
	public sealed class UriIdentifier : Identifier {
		/// <summary>
		/// The allowed protocol schemes in a URI Identifier.
		/// </summary>
		private static readonly string[] allowedSchemes = { "http", "https" };

		/// <summary>
		/// The special scheme to use for HTTP URLs that should not have their paths compressed.
		/// </summary>
		private static NonPathCompressingUriParser roundTrippingHttpParser = new NonPathCompressingUriParser(Uri.UriSchemeHttp);

		/// <summary>
		/// The special scheme to use for HTTPS URLs that should not have their paths compressed.
		/// </summary>
		private static NonPathCompressingUriParser roundTrippingHttpsParser = new NonPathCompressingUriParser(Uri.UriSchemeHttps);

		/// <summary>
		/// The special scheme to use for HTTP URLs that should not have their paths compressed.
		/// </summary>
		private static NonPathCompressingUriParser publishableHttpParser = new NonPathCompressingUriParser(Uri.UriSchemeHttp);

		/// <summary>
		/// The special scheme to use for HTTPS URLs that should not have their paths compressed.
		/// </summary>
		private static NonPathCompressingUriParser publishableHttpsParser = new NonPathCompressingUriParser(Uri.UriSchemeHttps);

		/// <summary>
		/// A value indicating whether scheme substitution is being used to workaround
		/// .NET path compression that invalidates some OpenIDs that have trailing periods
		/// in one of their path segments.
		/// </summary>
		private static bool schemeSubstitution;

		/// <summary>
		/// Initializes static members of the <see cref="UriIdentifier"/> class.
		/// </summary>
		/// <remarks>
		/// This method attempts to workaround the .NET Uri class parsing bug described here:
		/// https://connect.microsoft.com/VisualStudio/feedback/details/386695/system-uri-incorrectly-strips-trailing-dots?wa=wsignin1.0#tabs
		/// since some identifiers (like some of the pseudonymous identifiers from Yahoo) include path segments
		/// that end with periods, which the Uri class will typically trim off.
		/// </remarks>
		static UriIdentifier() {
			// Our first attempt to handle trailing periods in path segments is to leverage
			// full trust if it's available to rewrite the rules.
			// In fact this is the ONLY way in .NET 3.5 (and arguably in .NET 4.0) to send
			// outbound HTTP requests with trailing periods, so it's the only way to perform
			// discovery on such an identifier.
			try {
				UriParser.Register(roundTrippingHttpParser, "dnoarthttp", 80);
				UriParser.Register(roundTrippingHttpsParser, "dnoarthttps", 443);
				UriParser.Register(publishableHttpParser, "dnoahttp", 80);
				UriParser.Register(publishableHttpsParser, "dnoahttps", 443);
				roundTrippingHttpParser.Initialize(false);
				roundTrippingHttpsParser.Initialize(false);
				publishableHttpParser.Initialize(true);
				publishableHttpsParser.Initialize(true);
				schemeSubstitution = true;
			} catch (SecurityException) {
				// We must be running in partial trust.  Nothing more we can do.
				Logger.OpenId.Warn("Unable to coerce .NET to stop compressing URI paths due to partial trust limitations.  Some URL identifiers may be unable to complete login.");
				schemeSubstitution = false;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="UriIdentifier"/> class.
		/// </summary>
		/// <param name="uri">The value this identifier will represent.</param>
		internal UriIdentifier(string uri)
			: this(uri, false) {
			Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(uri));
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="UriIdentifier"/> class.
		/// </summary>
		/// <param name="uri">The value this identifier will represent.</param>
		/// <param name="requireSslDiscovery">if set to <c>true</c> [require SSL discovery].</param>
		internal UriIdentifier(string uri, bool requireSslDiscovery)
			: base(uri, requireSslDiscovery) {
			Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(uri));
			Uri canonicalUri;
			bool schemePrepended;
			if (!TryCanonicalize(uri, out canonicalUri, requireSslDiscovery, out schemePrepended)) {
				throw new UriFormatException();
			}
			if (requireSslDiscovery && canonicalUri.Scheme != Uri.UriSchemeHttps) {
				throw new ArgumentException(OpenIdStrings.ExplicitHttpUriSuppliedWithSslRequirement);
			}
			this.Uri = canonicalUri;
			this.SchemeImplicitlyPrepended = schemePrepended;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="UriIdentifier"/> class.
		/// </summary>
		/// <param name="uri">The value this identifier will represent.</param>
		internal UriIdentifier(Uri uri)
			: this(uri, false) {
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="UriIdentifier"/> class.
		/// </summary>
		/// <param name="uri">The value this identifier will represent.</param>
		/// <param name="requireSslDiscovery">if set to <c>true</c> [require SSL discovery].</param>
		internal UriIdentifier(Uri uri, bool requireSslDiscovery)
			: base(uri != null ? uri.OriginalString : null, requireSslDiscovery) {
			Contract.Requires<ArgumentNullException>(uri != null);

			string uriAsString = uri.OriginalString;
			if (schemeSubstitution) {
				uriAsString = NormalSchemeToSpecialRoundTrippingScheme(uriAsString);
			}

			if (!TryCanonicalize(new UriBuilder(uriAsString), out uri)) {
				throw new UriFormatException();
			}
			if (requireSslDiscovery && uri.Scheme != Uri.UriSchemeHttps) {
				throw new ArgumentException(OpenIdStrings.ExplicitHttpUriSuppliedWithSslRequirement);
			}
			this.Uri = uri;
			this.SchemeImplicitlyPrepended = false;
		}

		/// <summary>
		/// Gets the URI this instance represents.
		/// </summary>
		internal Uri Uri { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the scheme was missing when this 
		/// Identifier was created and added automatically as part of the 
		/// normalization process.
		/// </summary>
		internal bool SchemeImplicitlyPrepended { get; private set; }

		/// <summary>
		/// Converts a <see cref="UriIdentifier"/> instance to a <see cref="Uri"/> instance.
		/// </summary>
		/// <param name="identifier">The identifier to convert to an ordinary <see cref="Uri"/> instance.</param>
		/// <returns>The result of the conversion.</returns>
		public static implicit operator Uri(UriIdentifier identifier) {
			if (identifier == null) {
				return null;
			}
			return identifier.Uri;
		}

		/// <summary>
		/// Converts a <see cref="Uri"/> instance to a <see cref="UriIdentifier"/> instance.
		/// </summary>
		/// <param name="identifier">The <see cref="Uri"/> instance to turn into a <see cref="UriIdentifier"/>.</param>
		/// <returns>The result of the conversion.</returns>
		public static implicit operator UriIdentifier(Uri identifier) {
			if (identifier == null) {
				return null;
			}
			return new UriIdentifier(identifier);
		}

		/// <summary>
		/// Tests equality between this URI and another URI.
		/// </summary>
		/// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
		/// <returns>
		/// true if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>; otherwise, false.
		/// </returns>
		/// <exception cref="T:System.NullReferenceException">
		/// The <paramref name="obj"/> parameter is null.
		/// </exception>
		public override bool Equals(object obj) {
			UriIdentifier other = obj as UriIdentifier;
			if (obj != null && other == null && Identifier.EqualityOnStrings) { // test hook to enable MockIdentifier comparison
				other = Identifier.Parse(obj.ToString()) as UriIdentifier;
			}
			if (other == null) {
				return false;
			}
			return this.Uri == other.Uri;
		}

		/// <summary>
		/// Returns the hash code of this XRI.
		/// </summary>
		/// <returns>
		/// A hash code for the current <see cref="T:System.Object"/>.
		/// </returns>
		public override int GetHashCode() {
			return Uri.GetHashCode();
		}

		/// <summary>
		/// Returns the string form of the URI.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
		/// </returns>
		public override string ToString() {
			return Uri.AbsoluteUri;
		}

		/// <summary>
		/// Determines whether a URI is a valid OpenID Identifier (of any kind).
		/// </summary>
		/// <param name="uri">The URI to test for OpenID validity.</param>
		/// <returns>
		/// 	<c>true</c> if the identifier is valid; otherwise, <c>false</c>.
		/// </returns>
		/// <remarks>
		/// A valid URI is absolute (not relative) and uses an http(s) scheme.
		/// </remarks>
		internal static bool IsValidUri(string uri) {
			Uri normalized;
			bool schemePrepended;
			return TryCanonicalize(uri, out normalized, false, out schemePrepended);
		}

		/// <summary>
		/// Determines whether a URI is a valid OpenID Identifier (of any kind).
		/// </summary>
		/// <param name="uri">The URI to test for OpenID validity.</param>
		/// <returns>
		/// 	<c>true</c> if the identifier is valid; otherwise, <c>false</c>.
		/// </returns>
		/// <remarks>
		/// A valid URI is absolute (not relative) and uses an http(s) scheme.
		/// </remarks>
		internal static bool IsValidUri(Uri uri) {
			if (uri == null) {
				return false;
			}
			if (!uri.IsAbsoluteUri) {
				return false;
			}
			if (!IsAllowedScheme(uri)) {
				return false;
			}
			return true;
		}

		/// <summary>
		/// Returns an <see cref="Identifier"/> that has no URI fragment.
		/// Quietly returns the original <see cref="Identifier"/> if it is not
		/// a <see cref="UriIdentifier"/> or no fragment exists.
		/// </summary>
		/// <returns>
		/// A new <see cref="Identifier"/> instance if there was a
		/// fragment to remove, otherwise this same instance..
		/// </returns>
		internal override Identifier TrimFragment() {
			// If there is no fragment, we have no need to rebuild the Identifier.
			if (Uri.Fragment == null || Uri.Fragment.Length == 0) {
				return this;
			}

			// Strip the fragment.
			return new UriIdentifier(this.OriginalString.Substring(0, this.OriginalString.IndexOf('#')));
		}

		/// <summary>
		/// Converts a given identifier to its secure equivalent.
		/// UriIdentifiers originally created with an implied HTTP scheme change to HTTPS.
		/// Discovery is made to require SSL for the entire resolution process.
		/// </summary>
		/// <param name="secureIdentifier">The newly created secure identifier.
		/// If the conversion fails, <paramref name="secureIdentifier"/> retains
		/// <i>this</i> identifiers identity, but will never discover any endpoints.</param>
		/// <returns>
		/// True if the secure conversion was successful.
		/// False if the Identifier was originally created with an explicit HTTP scheme.
		/// </returns>
		internal override bool TryRequireSsl(out Identifier secureIdentifier) {
			// If this Identifier is already secure, reuse it.
			if (IsDiscoverySecureEndToEnd) {
				secureIdentifier = this;
				return true;
			}

			// If this identifier already uses SSL for initial discovery, return one
			// that guarantees it will be used throughout the discovery process.
			if (String.Equals(Uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
				secureIdentifier = new UriIdentifier(this.Uri, true);
				return true;
			}

			// Otherwise, try to make this Identifier secure by normalizing to HTTPS instead of HTTP.
			if (this.SchemeImplicitlyPrepended) {
				UriBuilder newIdentifierUri = new UriBuilder(this.Uri);
				newIdentifierUri.Scheme = Uri.UriSchemeHttps;
				if (newIdentifierUri.Port == 80) {
					newIdentifierUri.Port = 443;
				}
				secureIdentifier = new UriIdentifier(newIdentifierUri.Uri, true);
				return true;
			}

			// This identifier is explicitly NOT https, so we cannot change it.
			secureIdentifier = new NoDiscoveryIdentifier(this, true);
			return false;
		}

		/// <summary>
		/// Determines whether the given URI is using a scheme in the list of allowed schemes.
		/// </summary>
		/// <param name="uri">The URI whose scheme is to be checked.</param>
		/// <returns>
		/// 	<c>true</c> if the scheme is allowed; otherwise, <c>false</c>.
		/// 	<c>false</c> is also returned if <paramref name="uri"/> is null.
		/// </returns>
		private static bool IsAllowedScheme(string uri) {
			if (string.IsNullOrEmpty(uri)) {
				return false;
			}
			return Array.FindIndex(
				allowedSchemes,
				s => uri.StartsWith(s + Uri.SchemeDelimiter, StringComparison.OrdinalIgnoreCase)) >= 0;
		}

		/// <summary>
		/// Determines whether the given URI is using a scheme in the list of allowed schemes.
		/// </summary>
		/// <param name="uri">The URI whose scheme is to be checked.</param>
		/// <returns>
		/// 	<c>true</c> if the scheme is allowed; otherwise, <c>false</c>.
		/// 	<c>false</c> is also returned if <paramref name="uri"/> is null.
		/// </returns>
		private static bool IsAllowedScheme(Uri uri) {
			if (uri == null) {
				return false;
			}
			return Array.FindIndex(
				allowedSchemes,
				s => uri.Scheme.Equals(s, StringComparison.OrdinalIgnoreCase)) >= 0;
		}

		/// <summary>
		/// Tries to canonicalize a user-supplied identifier.
		/// This does NOT convert a user-supplied identifier to a Claimed Identifier!
		/// </summary>
		/// <param name="uri">The user-supplied identifier.</param>
		/// <param name="canonicalUri">The resulting canonical URI.</param>
		/// <param name="forceHttpsDefaultScheme">If set to <c>true</c> and the user-supplied identifier lacks a scheme, the "https://" scheme will be prepended instead of the standard "http://" one.</param>
		/// <param name="schemePrepended">if set to <c>true</c> [scheme prepended].</param>
		/// <returns>
		/// <c>true</c> if the identifier was valid and could be canonicalized.
		/// <c>false</c> if the identifier is outside the scope of allowed inputs and should be rejected.
		/// </returns>
		/// <remarks>
		/// Canonicalization is done by adding a scheme in front of an
		/// identifier if it isn't already present.  Other trivial changes that do not
		/// require network access are also done, such as lower-casing the hostname in the URI.
		/// </remarks>
		private static bool TryCanonicalize(string uri, out Uri canonicalUri, bool forceHttpsDefaultScheme, out bool schemePrepended) {
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(uri));

			uri = uri.Trim();
			canonicalUri = null;
			schemePrepended = false;
			try {
				// Assume http:// scheme if an allowed scheme isn't given, and strip
				// fragments off.  Consistent with spec section 7.2#3
				if (!IsAllowedScheme(uri)) {
					uri = (forceHttpsDefaultScheme ? Uri.UriSchemeHttps : Uri.UriSchemeHttp) +
						Uri.SchemeDelimiter + uri;
					schemePrepended = true;
				}

				if (schemeSubstitution) {
					uri = NormalSchemeToSpecialRoundTrippingScheme(uri);
				}

				// Use a UriBuilder because it helps to normalize the URL as well.
				return TryCanonicalize(new UriBuilder(uri), out canonicalUri);
			} catch (UriFormatException) {
				// We try not to land here with checks in the try block, but just in case.
				return false;
			}
		}

		/// <summary>
		/// Removes the fragment from a URL and sets the host to lowercase.
		/// </summary>
		/// <param name="uriBuilder">The URI builder with the value to canonicalize.</param>
		/// <param name="canonicalUri">The resulting canonical URI.</param>
		/// <returns><c>true</c> if the canonicalization was successful; <c>false</c> otherwise.</returns>
		/// <remarks>
		/// This does NOT standardize an OpenID URL for storage in a database, as
		/// it does nothing to convert the URL to a Claimed Identifier, besides the fact
		/// that it only deals with URLs whereas OpenID 2.0 supports XRIs.
		/// For this, you should lookup the value stored in IAuthenticationResponse.ClaimedIdentifier.
		/// </remarks>
		[SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "The user will see the result of this operation and they want to see it in lower case.")]
		private static bool TryCanonicalize(UriBuilder uriBuilder, out Uri canonicalUri) {
			Contract.Requires<ArgumentNullException>(uriBuilder != null);

			uriBuilder.Host = uriBuilder.Host.ToLowerInvariant();

			if (schemeSubstitution) {
				// Swap out our round-trippable scheme for the publishable (hidden) scheme.
				uriBuilder.Scheme = uriBuilder.Scheme == roundTrippingHttpParser.RegisteredScheme ? publishableHttpParser.RegisteredScheme : publishableHttpsParser.RegisteredScheme;
			}

			canonicalUri = uriBuilder.Uri;
			return true;
		}

		/// <summary>
		/// Gets the special non-compressing scheme or URL for a standard scheme or URL.
		/// </summary>
		/// <param name="normal">The ordinary URL or scheme name.</param>
		/// <returns>The non-compressing equivalent scheme or URL for the given value.</returns>
		private static string NormalSchemeToSpecialRoundTrippingScheme(string normal) {
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(normal));
			Contract.Requires<InternalErrorException>(schemeSubstitution);
			Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

			int delimiterIndex = normal.IndexOf(Uri.SchemeDelimiter);
			string normalScheme = delimiterIndex < 0 ? normal : normal.Substring(0, delimiterIndex);
			string nonCompressingScheme;
			if (string.Equals(normalScheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(normalScheme, publishableHttpParser.RegisteredScheme, StringComparison.OrdinalIgnoreCase)) {
				nonCompressingScheme = roundTrippingHttpParser.RegisteredScheme;
			} else if (string.Equals(normalScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(normalScheme, publishableHttpsParser.RegisteredScheme, StringComparison.OrdinalIgnoreCase)) {
				nonCompressingScheme = roundTrippingHttpsParser.RegisteredScheme;
			} else {
				throw new NotSupportedException();
			}

			return delimiterIndex < 0 ? nonCompressingScheme : nonCompressingScheme + normal.Substring(delimiterIndex);
		}

#if CONTRACTS_FULL
		/// <summary>
		/// Verifies conditions that should be true for any valid state of this object.
		/// </summary>
		[SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Called by code contracts.")]
		[SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by code contracts.")]
		[ContractInvariantMethod]
		private void ObjectInvariant() {
			Contract.Invariant(this.Uri != null);
			Contract.Invariant(this.Uri.AbsoluteUri != null);
		}
#endif

		/// <summary>
		/// A URI parser that does not compress paths, such as trimming trailing periods from path segments.
		/// </summary>
		private class NonPathCompressingUriParser : GenericUriParser {
			/// <summary>
			/// The field that stores the scheme that this parser is registered under.
			/// </summary>
			private static FieldInfo schemeField;

			/// <summary>
			/// The standard "http" or "https" scheme that this parser is subverting.
			/// </summary>
			private string standardScheme;

			/// <summary>
			/// Initializes a new instance of the <see cref="NonPathCompressingUriParser"/> class.
			/// </summary>
			/// <param name="standardScheme">The standard scheme that this parser will be subverting.</param>
			public NonPathCompressingUriParser(string standardScheme)
				: base(GenericUriParserOptions.DontCompressPath | GenericUriParserOptions.IriParsing | GenericUriParserOptions.Idn) {
				Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(standardScheme));
				this.standardScheme = standardScheme;
			}

			/// <summary>
			/// Gets the scheme this parser is registered under.
			/// </summary>
			/// <value>The registered scheme.</value>
			internal string RegisteredScheme { get; private set; }

			/// <summary>
			/// Initializes this parser with the actual scheme it should appear to be.
			/// </summary>
			/// <param name="hideNonStandardScheme">if set to <c>true</c> Uris using this scheme will look like they're using the original standard scheme.</param>
			/// <returns>
			/// A value indicating whether this parser will be able to complete its task.
			/// It can return <c>false</c> under partial trust conditions.
			/// </returns>
			internal void Initialize(bool hideNonStandardScheme) {
				if (schemeField == null) {
					schemeField = typeof(UriParser).GetField("m_Scheme", BindingFlags.NonPublic | BindingFlags.Instance);
				}

				this.RegisteredScheme = (string)schemeField.GetValue(this);

				if (hideNonStandardScheme) {
					schemeField.SetValue(this, this.standardScheme.ToLowerInvariant());
				}
			}
		}
	}
}
