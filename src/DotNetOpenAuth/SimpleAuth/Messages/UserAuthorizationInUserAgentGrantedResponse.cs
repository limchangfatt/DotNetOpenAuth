﻿//-----------------------------------------------------------------------
// <copyright file="UserAuthorizationInUserAgentGrantedResponse.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.SimpleAuth.Messages {
	using System;
	using DotNetOpenAuth.Messaging;

	/// <summary>
	/// The message sent by the Token Issuer to the Consumer via the user agent
	/// to indicate that user authorization was granted, and to return the user
	/// to the Consumer where they started their experience.
	/// </summary>
	internal class UserAuthorizationInUserAgentGrantedResponse : MessageBase, IDirectedProtocolMessage {
		/// <summary>
		/// Initializes a new instance of the <see cref="UserAuthorizationInUserAgentGrantedResponse"/> class.
		/// </summary>
		/// <param name="consumerCallback">The consumer callback.</param>
		/// <param name="version">The protocol version.</param>
		internal UserAuthorizationInUserAgentGrantedResponse(Uri consumerCallback, Version version)
			: base(version, MessageTransport.Indirect, consumerCallback) {
		}

		/// <summary>
		/// Gets or sets the verifier.
		/// </summary>
		/// <value>The verification code, if the user authorized the Consumer.</value>
		[MessagePart(Protocol.sa_verifier, IsRequired = true, AllowEmpty = true)]
		internal string Verifier { get; set; }

		/// <summary>
		/// Gets or sets the state of the consumer as provided by the consumer in the
		/// authorization request.
		/// </summary>
		/// <value>The state of the consumer.</value>
		/// <remarks>
		/// REQUIRED if the Consumer sent the value in the <see cref="UserAuthorizationRequestInUserAgentRequest"/>.
		/// </remarks>
		[MessagePart(Protocol.sa_consumer_state, IsRequired = false, AllowEmpty = true)]
		internal string ConsumerState { get; set; }
	}
}
