﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Merkurius
{
    namespace Layers
    {
        [DataContract]
        public class Softmax : Layer, IUpdatable
        {
            [DataMember]
            private double[] weights = null;
            [DataMember]
            private double[] biases = null;
            private Batch<double[]> internalInputs = null;
            private List<Tuple<double[], double[]>> gradientList = null;

            public double[] Weights
            {
                get
                {
                    return this.weights;
                }
                set
                {
                    this.weights = value;
                }
            }

            public double[] Biases
            {
                get
                {
                    return this.biases;
                }
                set
                {
                    this.biases = value;
                }
            }

            public Softmax(int inputs, int outputs, Func<int, int, double> func) : base(inputs, outputs)
            {
                var length = inputs * outputs;

                this.weights = new double[length];
                this.biases = new double[outputs];

                for (int i = 0; i < length; i++)
                {
                    this.weights[i] = func(inputs, outputs);
                }

                for (int i = 0; i < outputs; i++)
                {
                    this.biases[i] = 0.0;
                }
            }

            public override Batch<double[]> Forward(Batch<double[]> inputs, bool isTraining)
            {
                var parallelOptions = new ParallelOptions();
                var data = new double[inputs.Size][];

                this.internalInputs = inputs;

                parallelOptions.MaxDegreeOfParallelism = 2 * Environment.ProcessorCount;

                Parallel.ForEach<double[], List<Tuple<long, double[]>>>(inputs, parallelOptions, () => new List<Tuple<long, double[]>>(), (vector, state, index, local) =>
                {
                    double[] summations = new double[this.outputs];
                    double[] activations = new double[this.outputs];

                    for (int i = 0; i < this.outputs; i++)
                    {
                        double sum = 0.0;

                        for (int j = 0; j < this.inputs; j++)
                        {
                            sum += vector[j] * this.weights[this.outputs * j + i];
                        }

                        summations[i] = sum + this.biases[i];
                    }

                    for (int i = 0; i < this.outputs; i++)
                    {
                        activations[i] = SoftmaxFunction(summations, i);
                    }

                    local.Add(Tuple.Create<long, double[]>(index, activations));

                    return local;
                }, (local) =>
                {
                    lock (data)
                    {
                        local.ForEach(x =>
                        {
                            data[x.Item1] = x.Item2;
                        });
                    }
                });

                return new Batch<double[]>(data);
            }

            public override Batch<double[]> Backward(Batch<double[]> deltas)
            {
                var parallelOptions = new ParallelOptions();
                var tuple = Tuple.Create<double[][], double[][]>(new double[deltas.Size][], new double[deltas.Size][]);

                this.gradientList = new List<Tuple<double[], double[]>>();

                parallelOptions.MaxDegreeOfParallelism = 2 * Environment.ProcessorCount;

                Parallel.ForEach<double[], List<Tuple<long, double[], double[]>>>(deltas, parallelOptions, () => new List<Tuple<long, double[], double[]>>(), (vector1, state, index, local) =>
                {
                    var gradients = new double[this.inputs * this.outputs];
                    var vector2 = new double[this.inputs];

                    for (int i = 0, j = 0; i < this.inputs; i++)
                    {
                        double error = 0.0;

                        for (int k = 0; k < this.outputs; k++)
                        {
                            error += vector1[k] * this.weights[j];
                            gradients[j] = vector1[k] * this.internalInputs[index][i];
                            j++;
                        }

                        vector2[i] = error;
                    }

                    local.Add(Tuple.Create<long, double[], double[]>(index, vector2, gradients));

                    return local;
                }, (local) =>
                {
                    lock (tuple)
                    {
                        local.ForEach(x =>
                        {
                            tuple.Item1[x.Item1] = x.Item2;
                            tuple.Item2[x.Item1] = x.Item3;
                        });
                    }
                });

                for (int i = 0; i < deltas.Size; i++)
                {
                    this.gradientList.Add(Tuple.Create<double[], double[]>(tuple.Item2[i], deltas[i]));
                }

                return new Batch<double[]>(tuple.Item1);
            }

            public Batch<double[]> GetGradients()
            {
                return new Batch<double[]>(this.gradientList.ConvertAll<double[]>(x => x.Item1.Concat<double>(x.Item2).ToArray<double>()));
            }

            public void SetGradients(Func<bool, double, int, double> func)
            {
                this.gradientList.ForEach(x =>
                {
                    for (int i = 0; i < x.Item1.Length; i++)
                    {
                        x.Item1[i] = func(true, x.Item1[i], i);
                    }

                    for (int i = 0; i < x.Item2.Length; i++)
                    {
                        x.Item2[i] = func(false, x.Item2[i], i);
                    }
                });
            }

            public void Update(Batch<double[]> gradients, Func<double, double, double> func)
            {
                var length = this.inputs * this.outputs;

                for (int i = 1; i < gradients.Size; i++)
                {
                    for (int j = 0; j < length; j++)
                    {
                        gradients[0][j] += gradients[i][j];
                    }

                    for (int j = 0, k = length; j < this.outputs; j++, k++)
                    {
                        gradients[0][k] += gradients[i][k];
                    }
                }

                for (int i = 0; i < length; i++)
                {
                    this.weights[i] = func(this.weights[i], gradients[0][i] / gradients.Size);
                }

                for (int i = 0, j = length; i < this.outputs; i++, j++)
                {
                    this.biases[i] = func(this.biases[i], gradients[0][j] / gradients.Size);
                }
            }

            private double SoftmaxFunction(double[] x, int i)
            {
                double max = 0.0;
                double sum = 0.0;

                for (int j = 0; j < x.Length; j++)
                {
                    if (x[j] > max)
                    {
                        max = x[j];
                    }
                }

                for (int j = 0; j < x.Length; j++)
                {
                    sum += Math.Exp(x[j] - max);
                }

                return Math.Exp(x[i] - max) / sum;
            }

            private double[] DerivativeOfSoftmaxFunction(double[] outputs, double[] deltas)
            {
                double[] dx = new double[deltas.Length];
                double sum = 0.0;

                for (int i = 0; i < deltas.Length; i++)
                {
                    dx[i] = outputs[i] * deltas[i];
                    sum += dx[i];
                }

                for (int i = 0; i < deltas.Length; i++)
                {
                    dx[i] -= outputs[i] * sum;
                }

                return dx;
            }

            private double[] DerivativeOfSoftmaxFunction(double[] x, int i)
            {
                // yi(1 - yi) if i = j
                // -yiyj otherwise
                double[] vector = new double[x.Length];

                for (int j = 0; j < x.Length; j++)
                {
                    if (i == j)
                    {
                        vector[j] = x[i] * (1.0 - x[i]);
                    }
                    else
                    {
                        vector[j] = -x[j] * x[i];
                    }
                }

                return vector;
            }
        }
    }
}
