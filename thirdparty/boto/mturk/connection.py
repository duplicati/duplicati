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

import urllib
import xml.sax
from boto import handler
from boto.mturk.price import Price
from boto.mturk.question import QuestionForm
import boto.mturk.notification
from boto.connection import AWSQueryConnection
from boto.exception import EC2ResponseError
from boto.resultset import ResultSet

class MTurkConnection(AWSQueryConnection):
    
    APIVersion = '2006-10-31'
    SignatureVersion = '1'
    
    def __init__(self, aws_access_key_id=None, aws_secret_access_key=None,
                 is_secure=False, port=None, proxy=None, proxy_port=None,
                 proxy_user=None, proxy_pass=None, host='mechanicalturk.amazonaws.com', debug=0,
                 https_connection_factory=None):
        AWSQueryConnection.__init__(self, aws_access_key_id, aws_secret_access_key,
                                    is_secure, port, proxy, proxy_port, proxy_user, proxy_pass,
                                    host, debug, https_connection_factory)
    
    def get_account_balance(self):
        params = {}
        response = self.make_request('GetAccountBalance', params)
        body = response.read()
        if response.status == 200:
            rs = ResultSet([('AvailableBalance', Price),
                            ('OnHoldBalance', Price)])
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs
        else:
            raise EC2ResponseError(response.status, response.reason, body)
    
    def register_hit_type(self, title, description, reward, duration,
                          keywords=None, approval_delay=None, qual_req=None):
        """
        Register a new HIT Type
        \ttitle, description are strings
        \treward is a Price object
        \tduration can be an integer or string
        """
        params = {'Title' : title,
                  'Description' : description,
                  'AssignmentDurationInSeconds' : duration}
        params.update(MTurkConnection.get_price_as_price(reward).get_as_params('Reward'))
        response = self.make_request('RegisterHITType', params)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.HITTypeId
        else:
            raise EC2ResponseError(response.status, response.reason, body)
    
    def set_email_notification(hit_type, email):
        """
        Performs a SetHITTypeNotification operation to set email notification for a specified HIT type
        """
        assert type(hit_type) is str, "hit_type argument should be a string."
        
        params = {'HITTypeId': hit_type}
        
        notification_params = {'Destination': email,
                               'Transport': 'Email',
                               'Version': boto.mturk.notification.NOTIFICATION_VERSION }
        
        # Set up dict of 'Notification.1.Transport' etc. values
        notification_rest_params = {}
        num = 1
        for key in notification_params:
            notification_rest_params['Notification.%d.%s' % (num, key)] = notification_params[key]
        
        # Update main params dict
        params.update(notification_rest_params)
        
        # Execute operation
        response = self.make_request('SetHITTypeNotification', params)
        body = response.read()
        
        # From the Developer Guide:
        # A successful request for the SetHITTypeNotification operation will return with no errors. The 
        # response will include a SetHITTypeNotificationResult element, which contains the Request (if 
        # the Request response group is specified). The operation returns no other data.
        
        if response.status == 200: # Is this the correct way to test if it worked?
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            return rs.HITTypeId
        else:
            raise EC2ResponseError(response.status, response.reason, body)
    
    
    def create_hit(self, title=None, description=None, keywords=None, reward=0.00,
                   duration=60*60*24*7, approval_delay=None, qual_req=None, hit_type=None,
                   question=None, questions=None):
        """
        Creates a new HIT.
        Returns HITId as a string.
        See: http://docs.amazonwebservices.com/AWSMechanicalTurkRequester/2006-10-31/ApiReference_CreateHITOperation.html
        """
        
        # handle single or multiple questions
        if question is not None and questions is not None:
            raise ValueError("Must specify either question (single Question instance) or questions (list), but not both")
        if question is not None and questions is None:
            questions = [question]
        
        
        # Handle keywords
        final_keywords = MTurkConnection.get_keywords_as_string(keywords)
        
        # Handle price argument
        final_price = MTurkConnection.get_price_as_price(reward)
        
        # Set up QuestionForm data structure
        qf = QuestionForm(questions=questions)
        
        # Handle basic arguments and set up params dict
        params = {'Title': title,
                  'Description' : description,
                  'Keywords': final_keywords,
                  'AssignmentDurationInSeconds' : duration,
                  'Question': qf.get_as_xml() }
        
        if approval_delay is not None:
            params.update({'AutoApprovalDelayInSeconds': approval_delay })
        
        params.update(final_price.get_as_params('Reward'))
        
        # Handle optional hit_type argument
        if hit_type is not None:
            params.update({'HITTypeId': hit_type})
        
        # Submit
        response = self.make_request('CreateHIT', params)
        body = response.read()
        if response.status == 200:
            rs = ResultSet()
            h = handler.XmlHandler(rs, self)
            xml.sax.parseString(body, h)
            
            return rs.HITId
            #return rs # return entire ResultSet for testing purposes
        else:
            raise EC2ResponseError(response.status, response.reason, body)
    
    @staticmethod
    def get_keywords_as_string(keywords):
        """
        Returns a comma+space-separated string of keywords from either a list or a string
        """
        if type(keywords) is list:
            final_keywords = ', '.join(keywords)
        elif type(keywords) is str:
            final_keywords = keywords
        elif keywords is None:
            final_keywords = ""
        else:
            raise TypeError("keywords argument must be a string or a list of strings; got a %s" % type(keywords))
        return final_keywords
    
    @staticmethod
    def get_price_as_price(reward):
        """
        Returns a Price data structure from either a float or a Price
        """
        if isinstance(reward, Price):
            final_price = reward
        else:
            final_price = Price(reward)
        return final_price
