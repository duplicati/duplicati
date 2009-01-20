/******************************************************************************* 
 *  Licensed under the Apache License, Version 2.0 (the "License"); 
 *  
 *  You may not use this file except in compliance with the License. 
 *  You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0.html 
 *  This file is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
 *  CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 *  specific language governing permissions and limitations under the License.
 * ***************************************************************************** 
 * 
 *  Joel Wetzel
 *  Affirma Consulting
 *  jwetzel@affirmaconsulting.com
 * 
 */

using System;
using System.Collections.Generic;
using System.Text;
using Affirma.ThreeSharp.Model;
using Affirma.ThreeSharp.Statistics;

namespace Affirma.ThreeSharp
{
    /// <summary>
    /// Service type indicates which AWS service should be used
    /// </summary>
    public enum ThreeSharpServiceType 
    {
        S3,
        AWS100
    }

    /// <summary>
    /// The IThreeSharp interface defines the operations of an S3 service proxy.
    /// It is implemented by the ThreeSharpQuery class.
    /// A mock object could also be built that implements it, for the purposes of offline testing.
    /// </summary>
    public interface IThreeSharp
    {
        /// <summary>
        /// Adds a bucket to an S3 account
        /// </summary>
        BucketAddResponse BucketAdd(BucketAddRequest request);

        /// <summary>
        /// Returns a stream of XML, describing the contents of a bucket
        /// </summary>
        BucketListResponse BucketList(BucketListRequest request);

        /// <summary>
        /// Adds a AWS100 Distribution
        /// </summary>
        DistributionAddResponse DistributionAdd(DistributionAddRequest request);

        // <summary>
        /// Deletes a AWS100 Distribution
        /// </summary>
        DistributionDeleteResponse DistributionDelete(DistributionDeleteRequest request);

        /// <summary>
        /// Updates a AWS100 Distribution
        /// </summary>
        DistributionUpdateResponse DistributionUpdate(DistributionUpdateRequest request);

        /// <summary>
        /// Gets a AWS100 Distribution
        /// </summary>
        DistributionGetResponse DistributionGet(DistributionGetRequest request);

        /// <summary>
        /// Returns a stream of XML, describing the contents of a distribution
        /// </summary>
        DistributionListResponse DistributionList(DistributionListRequest request);

        /// <summary>
        /// Streams an object up to a bucket
        /// </summary>
        ObjectAddResponse ObjectAdd(ObjectAddRequest request);

        /// <summary>
        /// Streams an object down from a bucket
        /// </summary>
        ObjectGetResponse ObjectGet(ObjectGetRequest request);

        /// <summary>
        /// Copies an object
        /// </summary>
        ObjectCopyResponse ObjectCopy(ObjectCopyRequest request);

        /// <summary>
        /// Generates a URL to access an object in a bucket
        /// </summary>
        UrlGetResponse UrlGet(UrlGetRequest request);

        /// <summary>
        /// Returns a stream of XML, describing an object's ACL
        /// </summary>
        ACLGetResponse ACLGet(ACLGetRequest request);

        /// <summary>
        /// Changes an object's ACL
        /// </summary>
        ACLChangeResponse ACLChange(ACLChangeRequest request);

        /// <summary>
        /// Deletes an object from a bucket
        /// </summary>
        ObjectDeleteResponse ObjectDelete(ObjectDeleteRequest request);

        /// <summary>
        /// Deletes a bucket
        /// </summary>
        BucketDeleteResponse BucketDelete(BucketDeleteRequest request);

        /// <summary>
        /// Returns an array of Transfer objects, which contain statistics about a data transfer operation
        /// </summary>
        Transfer[] GetTransfers();

        /// <summary>
        /// Returns statistics about a single data transfer operation
        /// </summary>
        Transfer GetTransfer(String id);

        long GetTotalBytesUploaded();

        long GetTotalBytesDownloaded();

    }
}
