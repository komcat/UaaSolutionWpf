using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace UaaSolutionWpf.Motion
{
    public class Graph
    {
        private Dictionary<string, List<(string, int)>> _edges;

        public Graph()
        {
            _edges = new Dictionary<string, List<(string, int)>>();
        }

        public void AddEdge(string fromNode, string toNode, int weight)
        {
            if (!_edges.ContainsKey(fromNode))
            {
                _edges[fromNode] = new List<(string, int)>();
            }
            _edges[fromNode].Add((toNode, weight));

            // Since the graph is undirected, add the edge in the other direction as well
            if (!_edges.ContainsKey(toNode))
            {
                _edges[toNode] = new List<(string, int)>();
            }
            _edges[toNode].Add((fromNode, weight));
        }


        public List<string> ShortestPath(string start, string end)
        {
            var previousNodes = new Dictionary<string, string>();
            var distances = new Dictionary<string, int>();
            var nodes = new List<string>();

            foreach (var node in _edges)
            {
                if (node.Key == start)
                {
                    distances[node.Key] = 0;
                }
                else
                {
                    distances[node.Key] = int.MaxValue;
                }

                nodes.Add(node.Key);
            }

            while (nodes.Count != 0)
            {
                nodes.Sort((x, y) => distances[x] - distances[y]);

                var smallest = nodes[0];
                nodes.Remove(smallest);

                if (smallest == end)
                {
                    var path = new List<string>();
                    while (previousNodes.ContainsKey(smallest))
                    {
                        path.Add(smallest);
                        smallest = previousNodes[smallest];
                    }

                    path.Add(start);
                    path.Reverse();
                    return path;
                }

                if (distances[smallest] == int.MaxValue)
                {
                    break;
                }

                foreach (var neighbor in _edges[smallest])
                {
                    var alt = distances[smallest] + neighbor.Item2;
                    if (alt < distances[neighbor.Item1])
                    {
                        distances[neighbor.Item1] = alt;
                        previousNodes[neighbor.Item1] = smallest;
                    }
                }
            }

            return null; // No path found
        }

        public List<string> GetAllNodes()
        {
            return _edges.Keys.ToList();
        }
    }


}
