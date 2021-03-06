//
// TextEditor.cs
//
// Author:
//   Lluis Sanchez Gual
//   Michael Hutchinson
//
// Copyright (C) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.ComponentModel;

using Gtk;
using Gdk;

namespace MonoDevelop.Components.PropertyGrid.PropertyEditors
{
	[PropertyEditorType (typeof (string))]
	public class TextEditor: HBox, IPropertyEditor
	{
		EditSession session;
		bool disposed;
		string initialText;
		object currentValue;
		Entry entry;
		ComboBox combo;
		ListStore store;

		public void Initialize (EditSession session)
		{
			this.session = session;
			
			//if standard values are supported by the converter, then 
			//we list them in a combo
			if (session.Property.Converter.GetStandardValuesSupported (session))
			{
				store = new ListStore (typeof(string), typeof(object));

				//if converter doesn't allow nonstandard values, or can't convert from strings, don't have an entry
				if (session.Property.Converter.GetStandardValuesExclusive (session) || !session.Property.Converter.CanConvertFrom (session, typeof(string))) {
					combo = new ComboBox (store);
					var crt = new CellRendererText ();
					combo.PackStart (crt, true);
					combo.AddAttribute (crt, "text", 0);
				} else {
					combo = new ComboBoxEntry (store, 0);
					entry = ((ComboBoxEntry)combo).Entry;
					entry.HeightRequest = combo.SizeRequest ().Height;
				}

				PackStart (combo, true, true, 0);
				combo.Changed += TextChanged;
				
				//fill the list
				foreach (object stdValue in session.Property.Converter.GetStandardValues (session)) {
					store.AppendValues (session.Property.Converter.ConvertToString (session, stdValue), ObjectBox.Box (stdValue));
				}
				
				//a value of "--" gets rendered as a --, if typeconverter marked with UsesDashesForSeparator
				object[] atts = session.Property.Converter.GetType ()
					.GetCustomAttributes (typeof (StandardValuesSeparatorAttribute), true);
				if (atts.Length > 0) {
					string separator = ((StandardValuesSeparatorAttribute)atts[0]).Separator;
					combo.RowSeparatorFunc = (model, iter) => separator == ((string)model.GetValue (iter, 0));
				}
			}
			// no standard values, so just use an entry
			else {
				entry = new Entry ();
				PackStart (entry, true, true, 0);
			}

			//if we have an entry, fix it up a little
			if (entry != null) {
				entry.HasFrame = false;
				entry.Activated += TextChanged;
			}

			if (entry != null && ShouldShowDialogButton ()) {
				var button = new Button ("...");
				PackStart (button, false, false, 0);
				button.Clicked += ButtonClicked;
			}
			
			Spacing = 3;
			ShowAll ();
		}
		
		protected virtual bool ShouldShowDialogButton ()
		{
			//if the object's Localizable, show a dialog, since the text's likely to be more substantial
			var at = (LocalizableAttribute) session.Property.Attributes [typeof(LocalizableAttribute)];
			return (at != null && at.IsLocalizable);
		}
		
		void ButtonClicked (object s, EventArgs a)
		{
			using (var dlg = new TextEditorDialog ()) {
				dlg.TransientFor = Toplevel as Gtk.Window;
				dlg.Text = entry.Text;
				if (dlg.Run () == (int) ResponseType.Ok) {
					entry.Text = dlg.Text;
					TextChanged (null, null);
				}
			}
		}

		bool GetValue (out object value)
		{
			//combo box, just find the active value
			if (store != null && entry == null) {
				TreeIter it;
				if (combo.GetActiveIter (out it)) {
					value = ObjectBox.Unbox (store.GetValue (it, 1));
					return true;
				}
				value = null;
				return false;
			}

			var text = entry.Text;

			// combo plus entry, try to find matching value
			if (store != null) {
				TreeIter it;
				if (store.GetIterFirst (out it)) {
					do {
						if ((string)store.GetValue (it, 0) == text) {
							value = ObjectBox.Unbox (store.GetValue (it, 1));
							return false;
						}
					} while (store.IterNext (ref it));
				}
			}

			//finally, convert the value
			try {
				value = session.Property.Converter.ConvertFromString (session, entry.Text);
				return true;
			} catch {
				// Invalid format
				value = null;
				return false;
			}
		}
		
		void TextChanged (object s, EventArgs a)
		{
			//ignore if nothing changed
			if (entry != null) {
				if (initialText == entry.Text) {
					return;
				}
				initialText = entry.Text;
			}

			object val;
			if (GetValue (out val)) {
				currentValue = val;
				if (ValueChanged != null)
					ValueChanged (this, a);
				if (entry != null)
					entry.ModifyFg (StateType.Normal);
			} else {
				entry.ModifyFg (StateType.Normal, new Color (255, 0, 0));
			}
		}
		
		// Gets/Sets the value of the editor. If the editor supports
		// several value types, it is the responsibility of the editor 
		// to return values with the expected type.
		public object Value {
			get { return currentValue; }
			set {
				currentValue = value;
				if (combo != null) {
					int index;
					if (FindComboValue (value, out index)) {
						combo.Active = index;
						initialText = combo.ActiveText;
						return;
					}
				}
				if (entry != null) {
					string val = session.Property.Converter.ConvertToString (session, value);
					entry.Text = val ?? string.Empty;
					initialText = entry.Text;
				}
			}
		}

		bool FindComboValue (object val, out int index)
		{
			index = 0;
			TreeIter it;
			if (!store.GetIterFirst (out it)) {
				return false;
			}
			do {
				if (object.Equals (ObjectBox.Unbox (store.GetValue (it, 1)), val)) {
					return true;
				}
				index++;
			} while (store.IterNext (ref it));
			return false;
		}
		
		protected override void OnDestroyed ()
		{
			base.OnDestroyed ();
			((IDisposable)this).Dispose ();
		}

		void IDisposable.Dispose ()
		{
			if (!disposed && entry != null && initialText != entry.Text) {
				TextChanged (null, null);
			}
			disposed = true;
		}

		// To be fired when the edited value changes.
		public event EventHandler ValueChanged;

		//GTK# doesn't like it when you put a string in a column of type Object
		class ObjectBox
		{
			public object Value;
			public static object Box (object o)
			{
				if (o is string)
					return new ObjectBox { Value = o };
				return o;
			}
			public static object Unbox (object o)
			{
				var b = o as ObjectBox;
				if (b == null)
					return o;
				return b.Value;
			}
		}

	}
	
	public class StandardValuesSeparatorAttribute : Attribute
	{
		readonly string separator;
		
		public string Separator { get { return separator; } }
		
		public StandardValuesSeparatorAttribute (string separator)
		{
			this.separator = separator;
		}
	}
}
