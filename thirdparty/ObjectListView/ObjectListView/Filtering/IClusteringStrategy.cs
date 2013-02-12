/*
 * IClusterStrategy - Encapsulates the ability to create a list of clusters from an ObjectListView
 *
 * Author: Phillip Piper
 * Date: 4-March-2011 11:59 pm
 *
 * Change log:
 * 2011-03-04  JPP  - First version
 * 
 * Copyright (C) 2011 Phillip Piper
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * If you wish to use this code in a closed source application, please contact phillip_piper@bigfoot.com.
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace BrightIdeasSoftware{

    /// <summary>
    /// Implementation of this interface control the selecting of cluster keys
    /// and how those clusters will be presented to the user
    /// </summary>
    public interface IClusteringStrategy {

        /// <summary>
        /// Gets or sets the column upon which this strategy will operate
        /// </summary>
        OLVColumn Column { get; set; }

        /// <summary>
        /// Get the cluster key by which the given model will be partitioned by this strategy
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        object GetClusterKey(object model);

        /// <summary>
        /// Create a cluster to hold the given cluster key
        /// </summary>
        /// <param name="clusterKey"></param>
        /// <returns></returns>
        ICluster CreateCluster(object clusterKey);

        /// <summary>
        /// Gets the display label that the given cluster should use
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        string GetClusterDisplayLabel(ICluster cluster);
    }
}
