# Copyright (c) 2006,2007 Mitch Garnaat http://garnaat.org/
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

from boto.connection import AWSAuthConnection, AWSQueryConnection
import xml.sax
from boto.sqs.queue import Queue
from boto.sqs.message import Message
from boto.sqs.attributes import Attributes
from boto import handler
from boto.resultset import ResultSet
from boto.exception import SQSError

PERM_ReceiveMessage = 'ReceiveMessage'
PERM_SendMessage = 'SendMessage'
PERM_FullControl = 'FullControl'

AllPermissions = [PERM_ReceiveMessage, PERM_SendMessage, PERM_FullControl]
                 
class SQSQueryConnection(AWSQueryConnection):

    """
    This class uses the Query API (boo!) to SQS to access some of the
    new features which have not yet been added to the REST api (yeah!).
    """
    
    DefaultHost = 'queue.amazonaws.com'
    APIVersion = '2007-05-01'
    SignatureVersion = '1'
    DefaultContentType = 'text/plain'
    
    def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
                 is_secure=False, port=None, proxy=None, proxy_port=None,
                 host=DefaultHost, debug=0, https_connection_factory=None):
        AWSQueryConnection.__init__(self, aws_access_key_id,
                                    aws_secret_access_key,
                                    is_secure, port, proxy, proxy_port,
                                    host, debug, https_connection_factory)

    def get_queue_attributes(self, queue_url, attribute='All'):
        params = {'Attribute' : attribute}
        response = self.make_request('GetQueueAttributes', params, queue_url)
        body = response.read()
        if response.status == 200:
            attrs = Attributes()
            h = handler.XmlHandler(attrs, self)
            xml.sax.parseString(body, h)
            return attrs
        else:
            raise SQSError(response.status, response.reason, body)

    def set_queue_attribute(self, queue_url, attribute, value):
        params = {'Attribute' : attribute, 'Value' : value}
        response = self.make_request('SetQueueAttributes', params, queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            raise SQSError(response.status, response.reason, body)

    def change_message_visibility(self, queue_url, message_id, vtimeout):
        params = {'MessageId' : message_id,
                  'VisibilityTimeout' : vtimeout}
        response = self.make_request('ChangeMessageVisibility', params,
                                     queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            raise SQSError(response.status, response.reason, body)
        
    def add_grant(self, queue_url, permission, email_address=None, user_id=None):
        params = {'Permission' : permission}
        if user_id:
            params['Grantee.ID'] = user_id
        if email_address:
            params['Grantee.EmailAddress'] = email_address
        response = self.make_request('AddGrant', params, queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            raise SQSError(response.status, response.reason, body)
        
    def remove_grant(self, queue_url, permission, email_address=None, user_id=None):
        params = {'Permission' : permission}
        if user_id:
            params['Grantee.ID'] = user_id
        if email_address:
            params['Grantee.EmailAddress'] = email_address
        response = self.make_request('RemoveGrant', params, queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            raise SQSError(response.status, response.reason, body)
        
    def list_grants(self, queue_url, permission=None, email_address=None, user_id=None):
        params = {}
        if user_id:
            params['Grantee.ID'] = user_id
        if email_address:
            params['Grantee.EmailAddress'] = email_address
        if permission:
            params['Permission'] = permission
        response = self.make_request('ListGrants', params, queue_url)
        body = response.read()
        if response.status == 200:
            return body
        else:
            raise SQSError(response.status, response.reason, body)

    def receive_message(self, queue_url, number_messages=1,
                        visibility_timeout=None, message_class=Message):
        """
        This provides the same functionality as the read and get_messages methods
        of the queue object.  The only reason this is included here is that there is
        currently a bug in SQS that makes it impossible to read a message from a queue
        owned by someone else (even if you have been granted appropriate permissions)
        via the REST interface.  As it turns out, I need to be able to do this so until
        the REST interface gets fixed this is the workaround.
        """
        params = {'NumberOfMessages' : number_messages}
        if visibility_timeout:
            params['VisibilityTimeout'] = visibility_timeout
        response = self.make_request('ReceiveMessage', params, queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet([('Message', message_class)])
            h = handler.XmlHandler(rs, queue_url)
            xml.sax.parseString(body, h)
            if len(rs) == 1:
                return rs[0]
            else:
                return rs
        else:
            raise SQSError(response.status, response.reason, body)

    def delete_message(self, queue_url, message_id):
        """
        Because we have to use the Query interface to read messages from queues that
        we don't own, we also have to provide a way to delete those messages via Query.
        """
        params = {'MessageId' : message_id}
        response = self.make_request('DeleteMessage', params, queue_url)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.status
        else:
            raise SQSError(response.status, response.reason, body)
        
class SQSConnection(AWSAuthConnection):
    
    DefaultHost = 'queue.amazonaws.com'
    APIVersion = '2007-05-01'
    DefaultContentType = 'text/plain'
    
    def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
                 is_secure=False, port=None, proxy=None, proxy_port=None,
                 host=DefaultHost, debug=0, https_connection_factory=None):
        AWSAuthConnection.__init__(self, host,
                                   aws_access_key_id, aws_secret_access_key,
                                   is_secure, port, proxy, proxy_port, debug,
                                   https_connection_factory)
        self.query_conn = None

    def make_request(self, method, path, headers=None, data=''):
        # add auth header
        if headers == None:
            headers = {}

        if not headers.has_key('AWS-Version'):
            headers['AWS-Version'] = self.APIVersion

        if not headers.has_key('Content-Type'):
            headers['Content-Type'] = self.DefaultContentType

        return AWSAuthConnection.make_request(self, method, path,
                                              headers, data)

    def get_query_connection(self):
        if not self.query_conn:
            self.query_conn = SQSQueryConnection(self.aws_access_key_id,
                                                 self.aws_secret_access_key,
                                                 self.is_secure, self.port,
                                                 self.proxy, self.proxy_port,
                                                 self.server, self.debug,
                                                 self.https_connection_factory)
        return self.query_conn

    def get_all_queues(self, prefix=''):
        if prefix:
            path = '/?QueueNamePrefix=%s' % prefix
        else:
            path = '/'
        response = self.make_request('GET', path)
        body = response.read()
        if response.status >= 300:
            raise SQSError(response.status, response.reason, body)
        rs = ResultSet([('QueueUrl', Queue)])
        h = handler.XmlHandler(rs, self)
        xml.sax.parseString(body, h)
        return rs

    def get_queue(self, queue_name):
        i = 0
        rs = self.get_all_queues(queue_name)
        for q in rs:
            i += 1
        if i != 1:
            return None
        else:
            return q

    def get_queue_attributes(self, queue_url, attribute='All'):
        """
        Performs a GetQueueAttributes request and returns an Attributes
        instance (subclass of a Dictionary) holding the requested
        attribute name/value pairs.
        Inputs:
            queue_url - the URL of the desired SQS queue
            attribute - All|ApproximateNumberOfMessages|VisibilityTimeout
                        Default value is "All"
        Returns:
            An Attribute object which is a mapping type holding the
            requested name/value pairs
        """
        qc = self.get_query_connection()
        return qc.get_queue_attributes(queue_url, attribute)
    
    def set_queue_attribute(self, queue_url, attribute, value):
        """
        Performs a SetQueueAttributes request.
        Inputs:
            queue_url - The URL of the desired SQS queue
            attribute - The name of the attribute you want to set.  The
                        only valid value at this time is: VisibilityTimeout
                value - The new value for the attribute.
                        For VisibilityTimeout the value must be an
                        integer number of seconds from 0 to 86400.
        Returns:
            Boolean True if successful, otherwise False.
        """
        qc = self.get_query_connection()
        return qc.set_queue_attribute(queue_url, attribute, value)

    def change_message_visibility(self, queue_url, message_id, vtimeout):
        """
        Change the VisibilityTimeout for an individual message.
        Inputs:
            queue_url - The URL of the desired SQS queue
            message_id - The ID of the message whose timeout will be changed
            vtimeout - The new VisibilityTimeout value, in seconds
        Returns:
            Boolean True if successful, otherwise False
        Note: This functionality is also available as a method of the
              Message object.
        """
        qc = self.get_query_connection()
        return qc.change_message_visibility(queue_url, message_id, vtimeout)

    def add_grant(self, queue_url, permission, email_address=None, user_id=None):
        """
        Add a grant to a queue.
        Inputs:
            queue_url - The URL of the desired SQS queue
            permission - The permission being granted.  One of "ReceiveMessage", "SendMessage" or "FullControl"
            email_address - the email address of the grantee.  If email_address is supplied, user_id should be None
            user_id - The ID of the grantee.  If user_id is supplied, email_address should be None
        Returns:
            Boolean True if successful, otherwise False
        """
        qc = self.get_query_connection()
        return qc.add_grant(queue_url, permission, email_address, user_id)

    def remove_grant(self, queue_url, permission, email_address=None, user_id=None):
        """
        Remove a grant from a queue.
        Inputs:
            queue_url - The URL of the desired SQS queue
            permission - The permission being removed.  One of "ReceiveMessage", "SendMessage" or "FullControl"
            email_address - the email address of the grantee.  If email_address is supplied, user_id should be None
            user_id - The ID of the grantee.  If user_id is supplied, email_address should be None
        Returns:
            Boolean True if successful, otherwise False
        """
        qc = self.get_query_connection()
        return qc.remove_grant(queue_url, permission, email_address, user_id)

    def list_grants(self, queue_url, permission=None, email_address=None, user_id=None):
        """
        List the grants to a queue.
        Inputs:
            queue_url - The URL of the desired SQS queue
            permission - The permission granted.  One of "ReceiveMessage", "SendMessage" or "FullControl".
                         If supplied, only grants that allow this permission will be returned.
            email_address - the email address of the grantee.  If supplied, only grants related to this email
                            address will be returned
            user_id - The ID of the grantee.  If supplied, only grants related to his user_id will be returned.
        Returns:
            A string containing the XML Response elements describing the grants.
        """
        qc = self.get_query_connection()
        return qc.list_grants(queue_url, permission, email_address, user_id)

    def create_queue(self, queue_name, visibility_timeout=None):
        """
        Create a new queue.
        Inputs:
            queue_name - The name of the new queue
            visibility_timeout - (Optional) The default visibility
                                 timeout for the new queue.
        Returns:
            A new Queue object representing the newly created queue.
        """
        path = '/?QueueName=%s' % queue_name
        if visibility_timeout:
            path = path + '&DefaultVisibilityTimeout=%d' % visibility_timeout
        response = self.make_request('POST', path)
        body = response.read()
        if response.status >= 300:
            raise SQSError(response.status, response.reason, body)
        q = Queue(self)
        h = handler.XmlHandler(q, self)
        xml.sax.parseString(body, h)
        return q

    def delete_queue(self, queue, force_deletion=False):
        """
        Delete an SQS Queue.
        Inputs:
            queue - a Queue object representing the SQS queue to be deleted.
            force_deletion - (Optional) Normally, SQS will not delete a
                             queue that contains messages.  However, if
                             the force_deletion argument is True, the
                             queue will be deleted regardless of whether
                             there are messages in the queue or not.
                             USE WITH CAUTION.  This will delete all
                             messages in the queue as well.
        Returns:
            An empty ResultSet object.  Not sure why, actually.  It
            should probably return a Boolean indicating success or
            failure.
        """
        method = 'DELETE'
        path = queue.id
        if force_deletion:
            path = path + '?ForceDeletion=true'
        response = self.make_request(method, path)
        body = response.read()
        if response.status >= 300:
            raise SQSError(response.status, response.reason, body)
        rs = ResultSet()
        h = handler.XmlHandler(rs, self)
        xml.sax.parseString(body, h)
        return rs

