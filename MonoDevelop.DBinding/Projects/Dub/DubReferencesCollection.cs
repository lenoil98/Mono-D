//
// DubReferencesCollection.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2013 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using MonoDevelop.D.Building;
using System.IO;

namespace MonoDevelop.D.Projects.Dub
{
	public class DubReferencesCollection : DProjectReferenceCollection, IEnumerable<DubProjectDependency>
	{
		public new DubProject Owner {get{return base.Owner as DubProject;}}
		public override event EventHandler Update;

		Dictionary<string, DubProjectDependency> dependencies = new Dictionary<string, DubProjectDependency>();

		public DubReferencesCollection (DubProject prj) : base(prj)
		{
		}

		public override bool CanAdd {
			get {
				return false;
			}
		}

		public override bool CanDelete {
			get {
				return false;
			}
		}

		public override void DeleteProjectRef (string projectId)
		{
			throw new NotImplementedException ();
		}

		public override void FireUpdate ()
		{
			if (Update != null)
				Update (this, EventArgs.Empty);
		}

		public override bool HasReferences {
			get {
				return dependencies.Count > 0;
			}
		}

		public override string GetIncludeName (string path)
		{
			foreach (var kv in dependencies)
				if (kv.Value.Path == path)
					return kv.Key;
			return path;
		}

		public override IEnumerable<string> Includes {
			get {
				foreach (var kv in dependencies)
					if(kv.Value.Path != null && !kv.Value.Name.Contains(":"))
						yield return kv.Value.Path;

				foreach (var inc in Owner.GetAdditionalDubIncludes())
					yield return inc;
			}
		}

		public override IEnumerable<string> ReferencedProjectIds {
			get {
				var allProjects = Ide.IdeApp.Workspace.GetAllProjects ();
				foreach (var kv in dependencies){
					var depPath = kv.Value.Name.Split(':');
					if (depPath.Length > 1 && depPath [depPath.Length - 2] == Owner.Name) {
						var depName = depPath [depPath.Length - 1];
						foreach (var prj in allProjects)
							if (prj.Name == depName)
								yield return prj.ItemId;
					}
				}
			}
		}

		public override bool AddReference ()
		{
			throw new NotImplementedException ();
		}

		static Regex dubInstalledPackagesOutputRegex = new Regex ("  (?<name>.+) (?<version>.+): (?<path>.+)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture);

		public void DeserializeDubPrjDependencies(JsonReader j)
		{
			dependencies.Clear();
			FireUpdate ();
			bool tryFillRemainingPaths = false;

			while (j.Read () && j.TokenType != JsonToken.EndObject) {
				if (j.TokenType == JsonToken.PropertyName) {
					var depName = j.Value as string;
					string depVersion = null;
					string depPath = null;

					if (!j.Read ())
						throw new JsonReaderException ("Found EOF when parsing project dependency");

					if (j.TokenType == JsonToken.StartObject) {
						while (j.Read () && j.TokenType != JsonToken.EndObject) {
							if (j.TokenType == JsonToken.PropertyName) {
								switch (j.Value as string) {
									case "version":
										depVersion = j.ReadAsString ();
										break;
									case "path":
										depPath = j.ReadAsString ();
										break;
								}
							}
						}
					} else if (j.TokenType == JsonToken.String) {
						depVersion = j.Value as string;
						tryFillRemainingPaths = true;
					}

					dependencies [depName] = new DubProjectDependency { Name = depName, Version = depVersion, Path = depPath };
				}
			}

			if (tryFillRemainingPaths) {
				string err, outp = null;
				try{
					ProjectBuilder.ExecuteCommand (DubSettings.Instance.DubCommand, "list", Owner.BaseDirectory.ToString (), null, out err, out outp);
					// Backward compatiblity
					if (!string.IsNullOrWhiteSpace(err) || !TryInterpretDubListOutput(outp))
					{
						ProjectBuilder.ExecuteCommand(DubSettings.Instance.DubCommand, "list-installed", Owner.BaseDirectory.ToString(), null, out err, out outp);
						TryInterpretDubListOutput(outp);
					}
				}catch(Exception ex) {
					MonoDevelop.Core.LoggingService.LogError ("Error while resolving dub dependencies via executing 'dub list-installed'", ex);
				}
				
			}

			FireUpdate ();
		}

		bool TryInterpretDubListOutput(string outp)
		{
			bool ret = false;
			DubProjectDependency dep;
			if (string.IsNullOrEmpty(outp))
				return false;

			foreach (Match match in dubInstalledPackagesOutputRegex.Matches(outp))
			{
				ret = true;
				if (match.Success && dependencies.TryGetValue(match.Groups["name"].Value, out dep) &&
					(string.IsNullOrEmpty(dep.Version) || dep.Version == match.Groups["version"].Value) &&
					string.IsNullOrEmpty(dep.Path))
					dep.Path = match.Groups["path"].Value.Trim();

			}
			return ret;
		}

		public IEnumerator<DubProjectDependency> GetEnumerator()
		{
			return dependencies.Values.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}

