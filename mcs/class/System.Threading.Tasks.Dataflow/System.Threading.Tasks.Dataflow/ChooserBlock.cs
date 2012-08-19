// JoinBlock.cs
//
// Copyright (c) 2011 Jérémie "garuma" Laval
// Copyright (c) 2012 Petr Onderka
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

namespace System.Threading.Tasks.Dataflow {
	class ChooserBlock<T1, T2, T3> {
		class ChooseTarget<TMessage> : ITargetBlock<TMessage> {
			readonly ChooserBlock<T1, T2, T3> chooserBlock;
			readonly int index;
			readonly Action<TMessage> action;

			public ChooseTarget (ChooserBlock<T1, T2, T3> chooserBlock,
			                     int index, Action<TMessage> action)
			{
				this.chooserBlock = chooserBlock;
				this.index = index;
				this.action = action;
			}

			public DataflowMessageStatus OfferMessage (
				DataflowMessageHeader messageHeader, TMessage messageValue,
				ISourceBlock<TMessage> source, bool consumeToAccept)
			{
				if (!chooserBlock.canAccept)
					return DataflowMessageStatus.DecliningPermanently;

				bool lockTaken = false;
				try {
					chooserBlock.messageLock.Enter (ref lockTaken);
					if (!chooserBlock.canAccept)
						return DataflowMessageStatus.DecliningPermanently;

					if (consumeToAccept) {
						bool consummed;
						messageValue = source.ConsumeMessage (messageHeader, this, out consummed);
						if (!consummed)
							return DataflowMessageStatus.NotAvailable;
					}

					chooserBlock.canAccept = false;
				} finally {
					if (lockTaken)
						chooserBlock.messageLock.Exit ();
				}

				chooserBlock.MessageArrived (index, action, messageValue);
				return DataflowMessageStatus.Accepted;
			}

			public Task Completion {
				get { return null; }
			}

			public void Complete ()
			{
			}

			public void Fault (Exception exception)
			{
			}
		}

		readonly TaskCompletionSource<int> completion = new TaskCompletionSource<int> ();

		SpinLock messageLock;
		bool canAccept = true;

		public ChooserBlock (
			Action<T1> action1, Action<T2> action2, Action<T3> action3,
			DataflowBlockOptions dataflowBlockOptions)
		{
			Target1 = new ChooseTarget<T1> (this, 0, action1);
			Target2 = new ChooseTarget<T2> (this, 1, action2);
			if (action3 != null)
				Target3 = new ChooseTarget<T3> (this, 2, action3);

			if (dataflowBlockOptions.CancellationToken != CancellationToken.None)
				dataflowBlockOptions.CancellationToken.Register (Cancelled);
		}

		void Cancelled ()
		{
			if (!canAccept)
				return;

			bool lockTaken = false;
			try {
				messageLock.Enter (ref lockTaken);
				if (!canAccept)
					return;

				completion.SetCanceled ();

				canAccept = false;
			} finally {
				if (lockTaken)
					messageLock.Exit ();
			}
		}

		public void AllSourcesCompleted ()
		{
			Cancelled ();
		}

		void MessageArrived<TMessage> (
			int index, Action<TMessage> action, TMessage value)
		{
			try {
				action (value);
				completion.SetResult (index);
			} catch (Exception e) {
				completion.SetException (e);
			}
		}

		public ITargetBlock<T1> Target1 { get; private set; }

		public ITargetBlock<T2> Target2 { get; private set; }

		public ITargetBlock<T3> Target3 { get; private set; }

		public Task<int> Completion {
			get { return completion.Task; }
		}
	}
}