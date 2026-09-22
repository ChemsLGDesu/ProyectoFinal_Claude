using System;
using System.Collections.Generic;

namespace TTTXO.Core
{
    /// <summary>
    /// Local AI opponent. Resolves each turn with the win-now to block to positional
    /// cascade from design-doc.md section 3, using the same engine spec that will later
    /// port to Cloud Code's GetAiMove.
    /// </summary>
    public sealed class AiPlayer
    {
        // "Tope de candidatas evaluadas por nodo de busqueda: 12" (design-doc.md section 3).
        private const int MaxCandidatesPerNode = 12;

        // "Distancia Chebyshev <= 2 de alguna celda ocupada" (design-doc.md section 3).
        private const int CandidateChebyshevRadius = 2;

        // Large enough to dominate any heuristic score, small enough to stay a finite double.
        private const double WinScore = 1_000_000;

        private readonly Random random;

        /// <summary>Seeded RNG constructor, for deterministic/testable behaviour.</summary>
        public AiPlayer(int randomSeed)
        {
            random = new Random(randomSeed);
        }

        /// <summary>Time-based seed, for normal gameplay use.</summary>
        public AiPlayer() : this(Environment.TickCount)
        {
        }

        public (int Row, int Col) ChooseMove(MatchController match, AiParams aiParams)
        {
            if (match == null)
                throw new ArgumentNullException(nameof(match));
            if (aiParams == null)
                throw new ArgumentNullException(nameof(aiParams));
            if (match.Status != MatchStatus.InProgress)
                throw new InvalidOperationException("Cannot choose a move: match is not in progress.");

            BoardConfig config = match.Config;
            CellOwner self = match.CurrentPlayer;
            CellOwner opponent = Opposite(self);

            // Opening move on an empty board: center cell, or (even sizes) a random pick among
            // the 4 central cells (design-doc.md section 3). Picked directly and uniformly at
            // random here so it does not depend on how a search would break a symmetric tie.
            if (match.TurnCount == 0)
            {
                List<(int Row, int Col)> central = GetCentralCells(match.Board.Size);
                return central.Count == 1 ? central[0] : central[random.Next(central.Count)];
            }

            // Work on a clone so the search never mutates the live match board.
            BoardState workingBoard = match.Board.Clone();

            // Step 1: win now.
            (int Row, int Col)? winningMove = FindCompletingMove(workingBoard, config.WinLength, self);
            if (winningMove.HasValue && random.NextDouble() < aiParams.WinProbability)
                return winningMove.Value;

            // Step 2: block the opponent's immediate win.
            (int Row, int Col)? blockingMove = FindCompletingMove(workingBoard, config.WinLength, opponent);
            if (blockingMove.HasValue && random.NextDouble() < aiParams.BlockProbability)
                return blockingMove.Value;

            // Step 3: positional move (search, or uniform-random among candidates).
            return ChoosePositionalMove(workingBoard, config, aiParams, self, opponent);
        }

        private static (int Row, int Col)? FindCompletingMove(BoardState board, int winLength, CellOwner player)
        {
            for (int r = 0; r < board.Size; r++)
            {
                for (int c = 0; c < board.Size; c++)
                {
                    if (board.GetCell(r, c) != CellOwner.None)
                        continue;

                    board.SetCell(r, c, player);
                    IReadOnlyList<(int Row, int Col)> winLine = RulesEngine.FindWinningLine(board, r, c, winLength);
                    board.SetCell(r, c, CellOwner.None);

                    if (winLine != null)
                        return (r, c);
                }
            }

            return null;
        }

        private (int Row, int Col) ChoosePositionalMove(
            BoardState board, BoardConfig config, AiParams aiParams, CellOwner self, CellOwner opponent)
        {
            if (aiParams.SearchDepth <= 0)
            {
                List<(int Row, int Col)> candidates = GenerateCandidates(board);
                return candidates[random.Next(candidates.Count)];
            }

            List<(double Score, (int Row, int Col) Move)> ranked =
                SearchBestMoves(board, config, self, opponent, aiParams.SearchDepth);

            if (ranked.Count == 0)
                throw new InvalidOperationException("AI search produced no candidate moves.");

            if (ranked.Count > 1 && random.NextDouble() < aiParams.BlunderChance)
                return ranked[1].Move;

            return ranked[0].Move;
        }

        private static List<(double Score, (int Row, int Col) Move)> SearchBestMoves(
            BoardState board, BoardConfig config, CellOwner self, CellOwner opponent, int searchDepth)
        {
            List<(int Row, int Col)> candidates = GetTopCandidates(board, config, self, self, opponent, MaxCandidatesPerNode);
            var results = new List<(double Score, (int Row, int Col) Move)>(candidates.Count);

            double alpha = double.NegativeInfinity;
            const double beta = double.PositiveInfinity;

            foreach ((int Row, int Col) move in candidates)
            {
                board.SetCell(move.Row, move.Col, self);
                double score = Minimax(board, config, self, opponent, opponent, searchDepth - 1, alpha, beta, move);
                board.SetCell(move.Row, move.Col, CellOwner.None);

                results.Add((score, move));
                if (score > alpha) alpha = score;
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results;
        }

        private static double Minimax(
            BoardState board, BoardConfig config, CellOwner self, CellOwner opponent,
            CellOwner turnToMove, int remainingDepth, double alpha, double beta, (int Row, int Col) lastMove)
        {
            CellOwner lastMoveOwner = board.GetCell(lastMove.Row, lastMove.Col);
            IReadOnlyList<(int Row, int Col)> winLine =
                RulesEngine.FindWinningLine(board, lastMove.Row, lastMove.Col, config.WinLength);
            if (winLine != null)
                return lastMoveOwner == self ? WinScore : -WinScore;

            if (board.IsFull)
                return 0.0;

            if (remainingDepth <= 0)
                return BoardHeuristics.Evaluate(board, config, self, opponent);

            List<(int Row, int Col)> candidates = GetTopCandidates(board, config, turnToMove, self, opponent, MaxCandidatesPerNode);
            bool maximizing = turnToMove == self;
            double best = maximizing ? double.NegativeInfinity : double.PositiveInfinity;

            foreach ((int Row, int Col) move in candidates)
            {
                board.SetCell(move.Row, move.Col, turnToMove);
                double score = Minimax(board, config, self, opponent, Opposite(turnToMove), remainingDepth - 1, alpha, beta, move);
                board.SetCell(move.Row, move.Col, CellOwner.None);

                if (maximizing)
                {
                    if (score > best) best = score;
                    if (best > alpha) alpha = best;
                }
                else
                {
                    if (score < best) best = score;
                    if (best < beta) beta = best;
                }

                if (alpha >= beta)
                    break;
            }

            return best;
        }

        /// <summary>
        /// Empty cells within Chebyshev distance 2 of any occupied cell, capped to the
        /// <paramref name="cap"/> best-scoring ones for <paramref name="mover"/> (the mover
        /// wants the candidates that are best for itself, whether it is self or opponent).
        /// </summary>
        private static List<(int Row, int Col)> GetTopCandidates(
            BoardState board, BoardConfig config, CellOwner mover, CellOwner self, CellOwner opponent, int cap)
        {
            List<(int Row, int Col)> candidates = GenerateCandidates(board);
            if (candidates.Count <= cap)
                return candidates;

            bool moverIsSelf = mover == self;
            var scored = new List<(double Score, (int Row, int Col) Cell)>(candidates.Count);

            foreach ((int Row, int Col) cell in candidates)
            {
                board.SetCell(cell.Row, cell.Col, mover);
                double score = BoardHeuristics.Evaluate(board, config, self, opponent);
                board.SetCell(cell.Row, cell.Col, CellOwner.None);
                scored.Add((score, cell));
            }

            scored.Sort((a, b) => moverIsSelf ? b.Score.CompareTo(a.Score) : a.Score.CompareTo(b.Score));

            var result = new List<(int Row, int Col)>(cap);
            for (int i = 0; i < cap && i < scored.Count; i++)
                result.Add(scored[i].Cell);
            return result;
        }

        private static List<(int Row, int Col)> GenerateCandidates(BoardState board)
        {
            if (board.EmptyCellCount == board.Size * board.Size)
                return GetCentralCells(board.Size);

            var candidates = new List<(int Row, int Col)>();
            for (int r = 0; r < board.Size; r++)
            {
                for (int c = 0; c < board.Size; c++)
                {
                    if (board.GetCell(r, c) != CellOwner.None)
                        continue;
                    if (HasOccupiedNeighbor(board, r, c, CandidateChebyshevRadius))
                        candidates.Add((r, c));
                }
            }

            return candidates;
        }

        private static bool HasOccupiedNeighbor(BoardState board, int row, int col, int maxDistance)
        {
            for (int dr = -maxDistance; dr <= maxDistance; dr++)
            {
                for (int dc = -maxDistance; dc <= maxDistance; dc++)
                {
                    if (dr == 0 && dc == 0)
                        continue;

                    int r = row + dr;
                    int c = col + dc;
                    if (!board.IsInRange(r, c))
                        continue;
                    if (board.GetCell(r, c) != CellOwner.None)
                        return true;
                }
            }

            return false;
        }

        /// <summary>Opening move on an empty board: the single center cell, or (for even sizes) one
        /// of the 4 central cells chosen at random (design-doc.md section 3).</summary>
        private static List<(int Row, int Col)> GetCentralCells(int size)
        {
            var result = new List<(int Row, int Col)>();
            if (size % 2 == 1)
            {
                int mid = size / 2;
                result.Add((mid, mid));
            }
            else
            {
                int a = size / 2 - 1;
                int b = size / 2;
                result.Add((a, a));
                result.Add((a, b));
                result.Add((b, a));
                result.Add((b, b));
            }

            return result;
        }

        private static CellOwner Opposite(CellOwner owner) =>
            owner == CellOwner.X ? CellOwner.O : CellOwner.X;
    }
}
