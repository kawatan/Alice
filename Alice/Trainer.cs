﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Alice
{
    public interface ITrainer
    {
        void Train(Collection<Layer> layerList, Collection<double[,]> weightsCollection, IDictionary<double[], IEnumerable<double[]>> dictionary, int epochs);
    }
}