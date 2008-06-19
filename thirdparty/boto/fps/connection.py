# Copyright (c) 2008 Chris Moyer http://coredumped.org/
#
# Permission is hereby granted, free of charge, to any person obtaining a
# copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including
# without limitation the rights to use, copy, modify, merge, publish, dis-
# tribute, sublicense, and/or sell copies of the Software, and to permit
# persons to whom the Software is furnished to do so, subject to the fol-
# lowing conditions:
#
# The above copyright notice and this permission notice shall be included
# in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
# OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABIL-
# ITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
# SHALL THE AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.

import urllib
import xml.sax
import uuid
import boto
import boto.utils
import urllib
from boto import handler
from boto.connection import AWSQueryConnection
from boto.resultset import ResultSet
from boto.exception import FPSResponseError

class FPSConnection(AWSQueryConnection):

	APIVersion = '2007-01-08'
	SignatureVersion = '1'

	def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
						is_secure=True, port=None, proxy=None, proxy_port=None,
						host='fps.sandbox.amazonaws.com', debug=0,
						https_connection_factory=None):
		AWSQueryConnection.__init__(self, aws_access_key_id,
												aws_secret_access_key,
												is_secure, port, proxy, proxy_port,
												host, debug, https_connection_factory)
	
	def install_payment_instruction(self, instruction, token_type="Unrestricted", transaction_id=None):
		"""
		InstallPaymentInstruction
		instruction: The PaymentInstruction to send, for example: 
			MyRole=='Caller' orSay 'Roles do not match';
		token_type: Defaults to "Unrestricted"
		transaction_id: Defaults to a new ID
		"""

		if(transaction_id == None):
			transaction_id = uuid.uuid4()
		params = {}
		params['PaymentInstruction'] = instruction
		params['TokenType'] = token_type
		params['CallerReference'] = transaction_id
		response = self.make_request("InstallPaymentInstruction", params)
		return response
	
	def install_caller_instruction(self, token_type="Unrestricted", transaction_id=None):
		"""
		Set us up as a caller
		This will install a new caller_token into the FPS section.
		This should really only be called to regenerate the caller token.
		"""
		response = self.install_payment_instruction("MyRole=='Caller';", token_type=token_type, transaction_id=transaction_id)
		body = response.read()
		if(response.status == 200):
			rs = ResultSet()
			h = handler.XmlHandler(rs, self)
			xml.sax.parseString(body, h)
			caller_token = rs.TokenId
			try:
				boto.config.save_system_option("FPS", "caller_token", caller_token)
			except(IOError):
				boto.config.save_user_option("FPS", "caller_token", caller_token)
			return caller_token
		else:
			raise FPSResponseError(response.status, respons.reason, body)

	def install_recipient_instruction(self, token_type="Unrestricted", transaction_id=None):
		"""
		Set us up as a Recipient
		This will install a new caller_token into the FPS section.
		This should really only be called to regenerate the recipient token.
		"""
		response = self.install_payment_instruction("MyRole=='Recipient';", token_type=token_type, transaction_id=transaction_id)
		body = response.read()
		if(response.status == 200):
			rs = ResultSet()
			h = handler.XmlHandler(rs, self)
			xml.sax.parseString(body, h)
			recipient_token = rs.TokenId
			try:
				boto.config.save_system_option("FPS", "recipient_token", recipient_token)
			except(IOError):
				boto.config.save_user_option("FPS", "recipient_token", recipient_token)

			return recipient_token
		else:
			raise FPSResponseError(response.status, respons.reason, body)

	def make_url(self, returnURL, paymentReason, pipelineName, **params):
		"""
		Generate the URL with the signature required for a transaction
		"""
		params['callerKey'] = str(self.aws_access_key_id)
		params['returnURL'] = str(returnURL)
		params['paymentReason'] = str(paymentReason)
		params['pipelineName'] = pipelineName

		if(not params.has_key('callerReference')):
			params['callerReference'] = str(uuid.uuid4())

		url = ""
		keys = params.keys()
		keys.sort()
		for k in keys:
			url += "&%s=%s" % (k, urllib.quote_plus(str(params[k])))

		url = "/cobranded-ui/actions/start?%s" % ( url[1:])
		signature= boto.utils.encode(self.aws_secret_access_key, url, True)
		return "https://authorize.payments-sandbox.amazon.com%s&awsSignature=%s" % (url, signature)

	def make_payment(self, amount, sender_token, charge_fee_to="Recipient", reference=None, senderReference=None, recipientReference=None, senderDescription=None, recipientDescription=None, callerDescription=None, metadata=None, transactionDate=None):
		"""
		Make a payment transaction
		You must specify the amount and the sender token.
		"""
		params = {}
		params['RecipientTokenId'] = boto.config.get("FPS", "recipient_token")
		params['CallerTokenId'] = boto.config.get("FPS", "caller_token")
		params['SenderTokenId'] = sender_token
		params['TransactionAmount.Amount'] = str(amount)
		params['TransactionAmount.CurrencyCode'] = "USD"
		params['ChargeFeeTo'] = charge_fee_to

		if(transactionDate != None):
			params['TransactionDate'] = transactionDate
		if(senderReference != None):
			params['SenderReference'] = senderReference
		if(recipientReference != None):
			params['RecipientReference'] = recipientReference
		if(senderDescription != None):
			params['SenderDescription'] = senderDescription
		if(recipientDescription != None):
			params['RecipientDescription'] = recipientDescription
		if(callerDescription != None):
			params['CallerDescription'] = callerDescription
		if(metadata != None):
			params['MetaData'] = metadata
		if(transactionDate != None):
			params['TransactionDate'] = transactionDate
		if(reference == None):
			reference = uuid.uuid4()
		params['CallerReference'] = reference

		response = self.make_request("Pay", params)
		body = response.read()
		if(response.status == 200):
			rs = ResultSet()
			h = handler.XmlHandler(rs, self)
			xml.sax.parseString(body, h)
			return rs
		else:
			raise FPSResponseError(response.status, response.reason, body)
